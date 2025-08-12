using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CreateColumn.Service.Request;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace CreateColumn.Service
{
    public class BaseHttpService
    {
        public HttpWebResponse Response;
        public CookieContainer Cookie => Global.Cookie;

        public virtual T Send<T>(BaseRequest baseRequest)
        {
            try
            {
                string resultString = Send(baseRequest);

                T result = JsonConvert.DeserializeObject<T>(resultString, new JsonSerializerSettings() { ContractResolver = new DefaultContractResolver() { NamingStrategy = new CamelCaseNamingStrategy() } });
                return result;
            }
            catch (JsonException e)
            {
                MessageBox.Show(
                 $"敘述：{e.Message} \n",
                 "程式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return default;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public virtual string Send(BaseRequest baseRequest)
        {
            try
            {
                string pms = baseRequest.GetPropertiesObject();
                string url = baseRequest.FullUrl;

                if (!string.IsNullOrWhiteSpace(baseRequest.Param))
                    url = baseRequest.FullUrl + baseRequest.Param;

                return RequestBehavior(baseRequest.Method, url, pms);
            }
            catch (JsonException e)
            {
                MessageBox.Show(
                 $"敘述：{e.Message} \n",
                 "程式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return default;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        internal string RequestBehavior(Method method, string url, string pms, bool isneedsession = false, bool istreatment = false, bool isJson = true)
        {
            try
            {
                //创建HttpWebRequest对象 
                HttpWebRequest https = (HttpWebRequest)WebRequest.Create(url);
                https.Timeout = 1000;
                https.Method = method.ToString();
                https.CookieContainer = Cookie;

                //改接口需要Session来处理
                if (isneedsession)
                {
                    //https.Headers.Add("token", GlobalData.Token);
                }

                switch (https.Method.ToLower())
                {
                    case "post":
                    case "patch":
                        if (isJson)
                        {
                            https.ContentType = "application/json";
                            byte[] bytes = Encoding.UTF8.GetBytes(pms);
                            https.ContentLength = bytes.Length;
                            Stream reqstream = https.GetRequestStream();
                            reqstream.Write(bytes, 0, bytes.Length);
                        }
                        else
                        {
                            https.ContentType = "application/x-www-form-urlencoded";
                            using (StreamWriter sw = new StreamWriter(https.GetRequestStream()))
                            {
                                try
                                {
                                    sw.Write(pms);
                                }
                                catch
                                {
                                    throw new Exception("寫入請求資料數據錯誤");
                                }
                            }
                        }
                        break;
                    case "get":
                        https.Connection = "application/json";
                        break;
                    default:
                        https.Connection = "application/json";
                        break;
                }
                //获取请求返回的数据 
                Response = (HttpWebResponse)https.GetResponse();

                ///如果是需要处理头部的
                //if (istreatment)
                //{
                //    string cookies = response.Headers["Set-Cookie"];
                //    string[] strarray = cookies.Split(';');
                //    //数组处理
                //    if (strarray != null
                //        && strarray.Length > 0)
                //    {
                //        foreach (string arr in strarray)
                //        {
                //            if (arr.Contains("SESSION")
                //                || arr.Contains("session"))
                //            {
                //                string[] arrString = arr.Split('=');
                //                GlobalData.Token = arrString[1];//赋值SessionID
                //                break;
                //            }
                //        }
                //    }
                //}

                //读取返回的信息 
                using (StreamReader sr = new StreamReader(Response.GetResponseStream(), true))
                {
                    //获取响应格式信息
                    string result = sr.ReadToEnd();
                    return result;
                }
            }
            catch (WebException e)
            {
                //MessageBox.Show(e.Message, $"錯誤:{e.Status}", MessageBoxButton.OK, MessageBoxImage.Error);
                throw e;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        internal string RequestBehavior(Method method, string url, Stream body, Content content = Content.Json, string contentType = "application/octet-stream", bool isneedsession = false, bool istreatment = false)
        {
            try
            {
                //创建HttpWebRequest对象 
                HttpWebRequest https = (HttpWebRequest)WebRequest.Create(url);
                //方式方法
                https.Method = method.ToString();
                https.CookieContainer = Cookie;
                Stream requestStream = null;

                //改接口需要Session来处理
                if (isneedsession)
                {
                    //https.Headers.Add("token", GlobalData.Token);
                }

                switch (https.Method.ToLower())
                {
                    case "post":

                        switch (content)
                        {
                            case Content.Json:
                                https.ContentType = "application/json";
                                break;
                            case Content.File:
                                https.ContentType = contentType;
                                break;
                            case Content.MultipartForm:
                                https.ContentType = "application/x-www-form-urlencoded";
                                break;
                        }

                        https.ContentLength = body.Length;

                        requestStream = https.GetRequestStream();
                        body.Position = 0;
                        body.CopyTo(requestStream);

                        body.Dispose();
                        break;
                    case "get":
                        https.Connection = "application/json";
                        break;
                    case "put":

                        switch (content)
                        {
                            case Content.Json:
                                https.ContentType = "application/json";
                                break;
                            case Content.File:
                                https.ContentType = contentType;
                                break;
                            case Content.MultipartForm:
                                https.ContentType = "application/x-www-form-urlencoded";
                                break;
                        }

                        https.ContentLength = body.Length;

                        requestStream = https.GetRequestStream();
                        body.Position = 0;
                        body.CopyTo(requestStream);

                        body.Dispose();
                        break;
                }
                //获取请求返回的数据 
                Response = (HttpWebResponse)https.GetResponse();

                //读取返回的信息 
                using (StreamReader sr = new StreamReader(Response.GetResponseStream(), true))
                {
                    //获取响应格式信息
                    string result = sr.ReadToEnd();
                    return result;
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }

    public enum Method
    {
        GET,
        POST,
        PATCH,
        PUT,
        DELETE
    }

    public enum Content
    {
        Json,
        File,
        MultipartForm
    }
}
