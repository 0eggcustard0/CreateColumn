using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using JoinGeometryUtils.Service.Request;
using Newtonsoft.Json;
using Image = System.Windows.Controls.Image;

namespace JoinGeometryUtils.Service
{
    public abstract class FileRequest : BaseRequest
    {
        public override Method Method => Method.POST;
        public bool IsUploaded { get; set; } = false;

        private Stream _streamContent;
        public Stream StreamContent
        {
            set
            {
                _streamContent = value;
                IsDisposed = false;
                //OnContentSet();
            }
            get
            {
                return _streamContent;
            }
        }

        [Body]
        public string Mime { get; set; }

        public bool IsDisposed { get; private set; } = true;

        public void Dispose()
        {
            StreamContent.Dispose();
            IsDisposed = true;
        }

        public void Uploaded()
        {
            IsUploaded = true;
        }
    }

    public class UploadImage : FileRequest
    {

        public override string Route
        {
            get
            {
                return "/image";
            }
        }

        private BitmapImage image;
        public BitmapImage Image
        {
            get
            {
                if (!IsUploaded)
                {
                    image = new BitmapImage();
                    StreamContent.Seek(0, SeekOrigin.Begin);
                    image.BeginInit();
                    image.StreamSource = StreamContent;
                    image.CacheOption = BitmapCacheOption.None;
                    image.EndInit();
                }

                return image;
            }
        }


        public string RfiId;

    }
}
