using System;
using System.Collections.Generic;
using ServiceStack.Text.Json;
using ServiceStack.Text.Jsv;

namespace ServiceStack.Text.Common
{
    internal static class DeserializeTypeRefJsv
    {
        private static readonly JsvTypeSerializer Serializer = (JsvTypeSerializer)JsvTypeSerializer.Instance;

        static readonly ReadOnlyMemory<char> typeAttr = JsWriter.TypeAttr.AsMemory();

        internal static object StringToType(
            TypeConfig typeConfig,
            ReadOnlySpan<char> strType,
            EmptyCtorDelegate ctorFn,
            KeyValuePair<string, TypeAccessor>[] typeAccessors)
        {
            var index = 0;
            var type = typeConfig.Type;

            if (strType.IsEmpty)
                return null;

            //if (!Serializer.EatMapStartChar(strType, ref index))
            if (strType[index++] != JsWriter.MapStartChar)
                throw DeserializeTypeRef.CreateSerializationError(type, strType.ToString());

            if (JsonTypeSerializer.IsEmptyMap(strType)) 
                return ctorFn();


            object instance = null;
            var lenient = JsConfig.PropertyConvention == PropertyConvention.Lenient;

            var strTypeLength = strType.Length;
            while (index < strTypeLength)
            {
                var propertyName = Serializer.EatMapKey(strType, ref index);

                //Serializer.EatMapKeySeperator(strType, ref index);
                index++;

                var propertyValueStr = Serializer.EatValue(strType, ref index);
                var possibleTypeInfo = propertyValueStr != null && propertyValueStr.Length > 1;

                if (possibleTypeInfo && propertyName.Equals(typeAttr.Span, StringComparison.OrdinalIgnoreCase))
                {
                    var explicitTypeName = Serializer.ParseString(propertyValueStr);
                    var explicitType = JsConfig.TypeFinder(explicitTypeName);

                    if (explicitType == null || explicitType.IsInterface || explicitType.IsAbstract)
                    {
                        Tracer.Instance.WriteWarning("Could not find type: " + propertyValueStr.ToString());
                    }
                    else if (!type.IsAssignableFrom(explicitType))
                    {
                        Tracer.Instance.WriteWarning("Could not assign type: " + propertyValueStr.ToString());
                    }
                    else
                    {
                        JsWriter.AssertAllowedRuntimeType(explicitType);
                        instance = explicitType.CreateInstance();
                    }

                    if (instance != null)
                    {
                        //If __type info doesn't match, ignore it.
                        if (!type.IsInstanceOfType(instance))
                        {
                            instance = null;
                        }
                        else
                        {
                            var derivedType = instance.GetType();
                            if (derivedType != type)
                            {
                                var derivedTypeConfig = new TypeConfig(derivedType);
                                var map = DeserializeTypeRef.GetTypeAccessors(derivedTypeConfig, Serializer);
                                if (map != null)
                                {
                                    typeAccessors = map;
                                }
                            }
                        }
                    }

                    //Serializer.EatItemSeperatorOrMapEndChar(strType, ref index);
                    if (index != strType.Length) index++;

                    continue;
                }

                if (instance == null) instance = ctorFn();

                var typeAccessor = typeAccessors.Get(propertyName, lenient);

                var propType = possibleTypeInfo && propertyValueStr[0] == '_' ? TypeAccessor.ExtractType(Serializer, propertyValueStr) : null;
                if (propType != null)
                {
                    try
                    {
                        if (typeAccessor != null)
                        {
                            var parseFn = Serializer.GetParseStringSegmentFn(propType);
                            var propertyValue = parseFn(propertyValueStr);
                            if (typeConfig.OnDeserializing != null)
                                propertyValue = typeConfig.OnDeserializing(instance, propertyName.ToString(), propertyValue);
                            typeAccessor.SetProperty(instance, propertyValue);
                        }

                        //Serializer.EatItemSeperatorOrMapEndChar(strType, ref index);
                        if (index != strType.Length) index++;

                        continue;
                    }
                    catch (Exception e)
                    {
                        if (JsConfig.OnDeserializationError != null) JsConfig.OnDeserializationError(instance, propType, propertyName.ToString(), propertyValueStr.ToString(), e);
                        if (JsConfig.ThrowOnError) throw DeserializeTypeRef.GetSerializationException(propertyName.ToString(), propertyValueStr.ToString(), propType, e);
                        else Tracer.Instance.WriteWarning("WARN: failed to set dynamic property {0} with: {1}", propertyName.ToString(), propertyValueStr.ToString());
                    }
                }

                if (typeAccessor?.GetProperty != null && typeAccessor.SetProperty != null)
                {
                    try
                    {
                        var propertyValue = typeAccessor.GetProperty(propertyValueStr);
                        if (typeConfig.OnDeserializing != null)
                            propertyValue = typeConfig.OnDeserializing(instance, propertyName.ToString(), propertyValue);
                        typeAccessor.SetProperty(instance, propertyValue);
                    }
                    catch (NotSupportedException) { throw; }
                    catch (Exception e)
                    {
                        if (JsConfig.OnDeserializationError != null) JsConfig.OnDeserializationError(instance, propType ?? typeAccessor.PropertyType, propertyName.ToString(), propertyValueStr.ToString(), e);
                        if (JsConfig.ThrowOnError) throw DeserializeTypeRef.GetSerializationException(propertyName.ToString(), propertyValueStr.ToString(), propType, e);
                        else Tracer.Instance.WriteWarning("WARN: failed to set property {0} with: {1}", propertyName.ToString(), propertyValueStr.ToString());
                    }
                }
                else
                {
                    // the property is not known by the DTO
                    typeConfig.OnDeserializing?.Invoke(instance, propertyName.ToString(), propertyValueStr.ToString());
                }

                //Serializer.EatItemSeperatorOrMapEndChar(strType, ref index);
                if (index != strType.Length) index++;
            }

            return instance;
        }
    }

    //The same class above but JSON-specific to enable inlining in this hot class.
}