using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.Storage;
using Windows.Storage.Streams;

namespace RdxBrowser
{
    public class DataAccess
    {
        public async void CreateSettingsFile()
        {
            try
            {
                var storageFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("settings.xml");

                using (IRandomAccessStream writeStream = await storageFile.OpenAsync(FileAccessMode.ReadWrite))
                {

                    Stream s = writeStream.AsStreamForWrite();
                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Async = true;
                    settings.Indent = true;

                    using(XmlWriter writer = XmlWriter.Create(s, settings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("settings");
                        writer.WriteStartElement("history");
                        writer.WriteEndElement();
                        writer.WriteStartElement("bookmarks");
                        writer.WriteEndElement();
                        writer.WriteStartElement("searchengine");
                        writer.WriteStartElement("google");
                        writer.WriteAttributeString("prefix", "https://www.google.com/search?q=");
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                        writer.Flush();
                        await writer.FlushAsync();
                    }

                }
                await Windows.System.Launcher.LaunchFileAsync(storageFile);
            }
            catch 
            {

               
            }
        }
    }
}
