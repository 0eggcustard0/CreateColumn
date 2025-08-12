using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using CreateColumn.Model.ResponseModel;
using CreateColumn.Service;
using Newtonsoft.Json;

namespace CreateColumn.Service.Request
{
    public class ParamAttribute : Attribute { }
    public class BodyAttribute : Attribute { }
    public class DeletableAttribute : Attribute { }

    public abstract class BaseRequest
    {

        private string _basePath
        {
            get
            {
#if DEBUG
                return Properties.Settings.Default.API_ROOT_URL;
#else
                return Properties.Settings.Default.PUBLIC_API_ROOT_URL;
#endif
            }
        }

        public virtual string ServerIP
        {
            get { return _basePath; }
        }

        public abstract Method Method { get; }
        public abstract string Route { get; }
        public string FullUrl => new Uri(new Uri(_basePath), Route).AbsoluteUri;
        public string Param { get; set; }

        /// <summary>
        /// 屬性轉換成json
        /// </summary>
        /// <returns></returns>
        public string GetPropertiesObject()
        {
            StringBuilder getBuilder = new StringBuilder();
            StringBuilder sb = new StringBuilder();
            StringWriter sw = new StringWriter(sb);

            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                writer.WriteStartObject();

                var type = this.GetType();
                var propertyArray = type.GetProperties();
                if (propertyArray != null && propertyArray.Length > 0)
                {
                    foreach (PropertyInfo property in propertyArray)
                    {

                        dynamic pvalue = property.GetValue(this);
                        Type ptype = property.GetType();
                        //if (pvalue != null)
                        //{
                        //    //当参数作为Query类型是, 则进行拆解对象拼接字符串
                        //    StringBuilder pbuilder = new StringBuilder();
                        //    var QpropertyArray = pvalue.GetType().GetProperties();
                        //    if (QpropertyArray != null && QpropertyArray.Length > 0)
                        //    {
                        //        foreach (PropertyInfo Qproperty in QpropertyArray)
                        //        {
                        //            var Qprevent = Qproperty.GetCustomAttribute<PreventAttribute>();
                        //            if (Qprevent != null)
                        //                continue;
                        //            var Qpvalue = Qproperty.GetValue(pvalue);
                        //            if (Qpvalue != null && Qpvalue.ToString() != "")
                        //            {
                        //                if (getBuilder.ToString() == string.Empty) getBuilder.Append("?");
                        //                getBuilder.Append(Qproperty.Name + "=" + HttpUtility.UrlEncode(Convert.ToString(Qpvalue)) + "&");
                        //            }
                        //        }
                        //    }
                        //    getBuilder.Append(pbuilder.ToString());
                        //}
                        if (pvalue != null || property.GetCustomAttribute<DeletableAttribute>() != null)
                        {
                            if (property.GetCustomAttribute<ParamAttribute>() != null)
                            {
                                if (getBuilder.ToString() == string.Empty) getBuilder.Append("?");
                                getBuilder.Append($"&{Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1)}={HttpUtility.UrlEncode(Convert.ToString(pvalue))}&");

                            }
                            else if (property.GetCustomAttribute<BodyAttribute>() != null)
                            {
                                writer.WritePropertyName(Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1));

                                if (property.PropertyType.IsArray)
                                {
                                    writer.WriteStartArray();
                                    foreach (var v in pvalue)
                                    {
                                        writer.WriteValue(v);
                                    }
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    if (!property.PropertyType.Equals(typeof(string)) && !property.PropertyType.Equals(typeof(int)))
                                    {
                                        string o = (string)JsonConvert.SerializeObject(pvalue);


                                        writer.WriteRawValue(o);

                                    }
                                    else
                                    {
                                        writer.WriteValue(pvalue);

                                    }
                                }
                            }
                        }
                    }
                }
                writer.WriteEndObject();


                string getStr = getBuilder.ToString().Trim('&');
                if (!string.IsNullOrWhiteSpace(getStr))
                    Param = getStr;

                return sb.ToString();
            }
        }
    }


}
