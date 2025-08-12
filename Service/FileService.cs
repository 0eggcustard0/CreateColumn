using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CreateColumn.Service.Request;
using Newtonsoft.Json;

namespace CreateColumn.Service
{
    public class FileService : BaseHttpService
    {
        public void Send(FileRequest fileRequest)
        {
            string url = fileRequest.FullUrl;

            if (!string.IsNullOrWhiteSpace(fileRequest.Param))
            {
                url = fileRequest.FullUrl + fileRequest.Param;
            }

            RequestBehavior(Method.POST, url, fileRequest.GetPropertiesObject());
            RequestBehavior(Method.PUT, fileRequest.ServerIP + Response.GetResponseHeader("Location"), fileRequest.StreamContent, Content.File, fileRequest.Mime);

            fileRequest.Uploaded();
        }
    }
}
