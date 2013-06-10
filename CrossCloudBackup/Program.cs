using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
//using Rackspace;
using Rackspace.CloudFiles;
using Rackspace.CloudFiles.Domain;
using Rackspace.CloudFiles.Domain.Request;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Reflection;

namespace CrossCloudBackup
{
    static class Program
    {
        private static AmazonS3 S3Client;
        private static Connection RSConnection;

        private static DateTime startTime;

        private static int intTransferred = 0;
        private static int intSkipped = 0;
        private static long bytesTransferred = 0;

        private static bool keepRunning = true;
        
        static void Main(string[] args)
        {
            startTime = DateTime.Now;
            
            //Catch exceptions
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MyHandler);

            //Catch ctrl+c to we can put out our summary
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = false;
                WriteLog("!!CANCELLED!!");
                keepRunning = false;
                PrintSummary();
            };

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;

            string strConfigFile = "CrossCloudBackup.xml";

            if (args.Length > 0) strConfigFile = args[0];

            if (!File.Exists(strConfigFile))
            {

                new XDocument(
                    new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("CrossCloudBackup Local Config File"),
                    new XElement("root",
                        new XElement("AWSKey", "someValue"),
                        new XElement("AWSSecret", "someValue"),
                        new XElement("AWSRegion", "eu-west-1"),
                        new XElement("RSUsername", "someValue"),
                        new XElement("RSAPIKey", "someValue"),
                        new XElement("RSUseServiceNet", "false"),
                        new XElement("RSLocation", "UK"),
                        new XElement("ExcludeBuckets", ""),
                        new XElement("ExcludeContainers", ""),
                        //new XElement("MirrorAll", "true"),  /TODO: Add Selective Sync
                        new XElement("RSBackupContainer", "s3-backup"),
                        new XElement("S3BackupBucket", "rs-backup")
                        //new XElement("TransferThreads", "3") //TODO: Add Threading
                    )
                )
                .Save(strConfigFile);
                Console.WriteLine(strConfigFile + " not found, blank one created.");
                Console.WriteLine("Press enter to exit...");
                Console.ReadLine();
                Environment.Exit(1);
            }

            //We know the config file exists, so open and read values
            XDocument config = XDocument.Load(strConfigFile);

            //Get AWS config
            string AWSKey = config.Element("root").Element("AWSKey").Value;
            string AWSSecret = config.Element("root").Element("AWSSecret").Value;
            RegionEndpoint region = RegionEndpoint.EUWest1;
            switch (config.Element("root").Element("AWSRegion").Value)
            {
                case "eu-west-1":
                    region = RegionEndpoint.EUWest1;
                    break;
                case "sa-east-1":
                    region = RegionEndpoint.SAEast1;
                    break;
                case "us-east-1":
                    region = RegionEndpoint.USEast1;
                    break;
                case "ap-northeast-1":
                    region = RegionEndpoint.APNortheast1;
                    break;
                case "us-west-2":
                    region = RegionEndpoint.USWest2;
                    break;
                case "us-west-1":
                    region = RegionEndpoint.USWest1;
                    break;
                case "ap-southeast-1":
                    region = RegionEndpoint.APSoutheast1;
                    break;
                case "ap-southeast-2":
                    region = RegionEndpoint.APSoutheast2;
                    break;
                default:
                    region = RegionEndpoint.EUWest1;
                    break;
            }

            //Create a connection to S3
            WriteLog("Connecting to S3");
            S3Client = AWSClientFactory.CreateAmazonS3Client(AWSKey, AWSSecret, region);

            //Get RS config
            Rackspace.CloudFiles.Utils.AuthUrl rsRegion  = Rackspace.CloudFiles.Utils.AuthUrl.US;
            switch (config.Element("root").Element("RSLocation").Value)
            {
                case "UK":
                    rsRegion = Rackspace.CloudFiles.Utils.AuthUrl.UK;
                    break;
                case "US":
                    rsRegion = Rackspace.CloudFiles.Utils.AuthUrl.US;
                    break;
                case "Mosso":
                    rsRegion = Rackspace.CloudFiles.Utils.AuthUrl.Mosso;
                    break;
            }

            //Create connection to Rackspace
            WriteLog("Connecting to Rackspace Cloud Files");
            RSConnection = new Connection(new UserCredentials(config.Element("root").Element("RSUsername").Value, config.Element("root").Element("RSAPIKey").Value, rsRegion), Convert.ToBoolean(config.Element("root").Element("RSUseServiceNet").Value));

            //Get exclusions
            string[] excludeBuckets = config.Element("root").Element("ExcludeBuckets").Value.Split(',');
            string[] excludeContainers = config.Element("root").Element("ExcludeContainers").Value.Split(',');

            //First process all the S3 buckets and stream right into Rackspace container.
            WriteLog("Listing S3 Buckets");
            ListBucketsResponse response = S3Client.ListBuckets();
            WriteLog("Found " + response.Buckets.Count() + " buckets");
            foreach (S3Bucket bucket in response.Buckets)
            {
                if (bucket.BucketName == config.Element("root").Element("S3BackupBucket").Value)
                {
                    WriteLog("Skipping " + bucket.BucketName + " as backup folder");
                }
                else if (excludeBuckets.Contains(bucket.BucketName)) {
                    WriteLog("Skipping " + bucket.BucketName + " as in exclusions");
                }
                else
                {
                    //We need to know if the bucket is in the right region, otherwise it will error
                    GetBucketLocationResponse locResponse = S3Client.GetBucketLocation(new GetBucketLocationRequest().WithBucketName(bucket.BucketName));
                    if (locResponse.Location == config.Element("root").Element("AWSRegion").Value)
                    {
                        WriteLog("Processing " + bucket.BucketName);
                        //Get list of files
                        ListObjectsRequest request = new ListObjectsRequest();
                        request.BucketName = bucket.BucketName;
                        do
                        {
                            ListObjectsResponse filesResponse = S3Client.ListObjects(request);
                            WriteLog("Found " + filesResponse.S3Objects.Count() + " files");
                            if (filesResponse.IsTruncated) WriteLog("there are additional pages of files");
                            foreach (S3Object file in filesResponse.S3Objects)
                            {
                                bool bolTransfer = false;
                                //See if it exists on Rackspace
                                string uri = RSConnection.StorageUrl + "/" + config.Element("root").Element("RSBackupContainer").Value + "/" + bucket.BucketName + "/" + file.Key;
                                try
                                {
                                    var req = (HttpWebRequest)WebRequest.Create(uri);
                                    req.Headers.Add("X-Auth-Token", RSConnection.AuthToken);
                                    req.Method = "HEAD";

                                    //Compare Etags to see if we need to sync
                                    using (var resp = req.GetResponse() as HttpWebResponse)
                                    {
                                        if ("\"" + resp.Headers["eTag"] + "\"" != file.ETag) bolTransfer = true;
                                    }
                                }
                                catch (System.Net.WebException e)
                                {
                                    if (e.Status == WebExceptionStatus.ProtocolError && ((HttpWebResponse)e.Response).StatusCode == HttpStatusCode.NotFound)
                                    {
                                        //Item not found, so upload
                                        bolTransfer = true;
                                    }
                                    //WriteLog("End Request to " + uri);
                                }
                                if (file.StorageClass == "GLACIER") bolTransfer = false; //We can't get things out of Glacier, but they aer still listed here.

                                if (bolTransfer)
                                {
                                    WriteLog("Syncing " + file.Key);
                                    using (GetObjectResponse getResponse = S3Client.GetObject(new GetObjectRequest().WithBucketName(bucket.BucketName).WithKey(file.Key)))
                                    {
                                        using (Stream s = getResponse.ResponseStream)
                                        {
                                            //We can stream right from s3 to CF, no need to store in memory or filesystem.                                            
                                            var req = (HttpWebRequest)WebRequest.Create(uri);
                                            req.Headers.Add("X-Auth-Token", RSConnection.AuthToken);
                                            req.Method = "PUT";
                                            req.SendChunked = true;
                                            req.AllowWriteStreamBuffering = false;
                                            req.Timeout = -1;

                                            using (Stream stream = req.GetRequestStream())
                                            {
                                                byte[] data = new byte[8192];
                                                int bytesRead = 0;
                                                while ((bytesRead = s.Read(data, 0, data.Length)) > 0)
                                                {
                                                    stream.Write(data, 0, bytesRead);
                                                }
                                                stream.Flush();
                                                stream.Close();
                                            }
                                            req.GetResponse().Close();
                                        }
                                    }
                                    intTransferred++;
                                    bytesTransferred += file.Size;
                                }
                                else
                                {
                                    WriteLog("Skipping " + file.Key);
                                    intSkipped++;
                                }

                                //Check our exit condition
                                if (!keepRunning) break;
                            }

                            //Loop if there is more than 1000 files
                            if (filesResponse.IsTruncated)
                            {
                                request.Marker = filesResponse.NextMarker;
                            }
                            else
                            {
                                request = null;
                            }

                            if (!keepRunning) break;
                        } while (request != null);
                    }
                }
                if (!keepRunning) break;
            }

            //Now get all the Rackspace containers and stream them to Amazon
            WriteLog("Listing CF Containers");
            List<string> lstContainers = RSConnection.GetContainers();
            WriteLog("Found " + lstContainers.Count() + " containers");
            foreach (string container in lstContainers) {
                if (container == config.Element("root").Element("RSBackupContainer").Value)
                {
                    WriteLog("Skipping " + container + " as backup folder");
                }
                else if (excludeContainers.Contains(container))
                {
                    WriteLog("Skipping " + container + " as in exclusions");
                }
                else
                {
                    WriteLog("Processing " + container);
                    
                    XmlDocument containerInfo = RSConnection.GetContainerInformationXml(container);
                    do
                    {
                        int filesCount = containerInfo.GetElementsByTagName("object").Count;
                        WriteLog("Found " + filesCount + " files");
                        foreach (XmlNode file in containerInfo.GetElementsByTagName("object"))
                        {
                            bool bolTransfer = false;
                            string strBucketName = config.Element("root").Element("S3BackupBucket").Value;
                            string strKey = container + file.SelectSingleNode("name").InnerText;
                            //See if the file exists on s3
                            try
                            {
                                GetObjectMetadataResponse metaResp = S3Client.GetObjectMetadata(new GetObjectMetadataRequest().WithBucketName(strBucketName).WithKey(strKey));
                                //Compare the etags
                                if (metaResp.ETag != "\"" + file.SelectSingleNode("hash").InnerText + "\"") bolTransfer = true;
                            }
                            catch (Amazon.S3.AmazonS3Exception e)
                            {
                                bolTransfer = true;
                            }

                            if (bolTransfer)
                            {
                                WriteLog("Syncing " + file.SelectSingleNode("name").InnerText);

                                //God the C# binding sucks, so let's stream manually
                                string uri = RSConnection.StorageUrl + "/" + container + "/" + file.SelectSingleNode("name").InnerText;
                                var req = (HttpWebRequest)WebRequest.Create(uri);
                                req.Headers.Add("X-Auth-Token", RSConnection.AuthToken);
                                req.Method = "GET";

                                using (var resp = req.GetResponse() as HttpWebResponse)
                                {
                                    using (Stream s = resp.GetResponseStream())
                                    {
                                        string today = String.Format("{0:ddd,' 'dd' 'MMM' 'yyyy' 'HH':'mm':'ss' 'zz00}", DateTime.Now);

                                        string stringToSign = "PUT\n" +
                                            "\n" +
                                            file.SelectSingleNode("content_type").InnerText + "\n" +
                                            "\n" +
                                            "x-amz-date:" + today + "\n" +
                                            "/" + strBucketName + "/" + strKey;

                                        Encoding ae = new UTF8Encoding();
                                        HMACSHA1 signature = new HMACSHA1(ae.GetBytes(AWSSecret));
                                        string encodedCanonical = Convert.ToBase64String(signature.ComputeHash(ae.GetBytes(stringToSign)));

                                        string authHeader = "AWS " + AWSKey + ":" + encodedCanonical;

                                        string uriS3 = "https://" + strBucketName + ".s3.amazonaws.com/" + strKey;
                                        var reqS3 = (HttpWebRequest)WebRequest.Create(uriS3);
                                        reqS3.Headers.Add("Authorization", authHeader);
                                        reqS3.Headers.Add("x-amz-date", today);
                                        reqS3.ContentType = file.SelectSingleNode("content_type").InnerText;
                                        reqS3.ContentLength = Convert.ToInt32(file.SelectSingleNode("bytes").InnerText);
                                        reqS3.Method = "PUT";

                                        reqS3.AllowWriteStreamBuffering = false;
                                        if (reqS3.ContentLength == -1L)
                                            reqS3.SendChunked = true;


                                        using (Stream streamS3 = reqS3.GetRequestStream())
                                        {
                                            byte[] data = new byte[32768];
                                            int bytesRead = 0;
                                            while ((bytesRead = s.Read(data, 0, data.Length)) > 0)
                                            {
                                                streamS3.Write(data, 0, bytesRead);
                                            }
                                            streamS3.Flush();
                                            streamS3.Close();
                                        }

                                        reqS3.GetResponse().Close();
      
                                    }

                                }


                                intTransferred++;
                                bytesTransferred += Convert.ToInt64(file.SelectSingleNode("bytes").InnerText);
                            }
                            else
                            {
                                WriteLog("Skipping " + file.SelectSingleNode("name").InnerText);
                                intSkipped++;
                            }

                            //Check our exit condition
                            if (!keepRunning) break;

                        }

                        if (filesCount < 10000)
                        {
                            containerInfo = null;
                        }
                        else
                        {
                            //Fetch the next list, but the Rackspace binding doesn't support markers with XML responses....
                            try
                            {
                                string uri = RSConnection.StorageUrl + "/" + container + "?format=xml&marker=" + Uri.EscapeUriString(containerInfo.FirstChild.NextSibling.LastChild.SelectSingleNode("name").InnerText);
                                var req = (HttpWebRequest)WebRequest.Create(uri);
                                req.Headers.Add("X-Auth-Token", RSConnection.AuthToken);
                                req.Method = "GET";

                                using (var resp = req.GetResponse() as HttpWebResponse)
                                {
                                    using (var reader = new System.IO.StreamReader(resp.GetResponseStream(), ASCIIEncoding.ASCII))
                                    {
                                        string responseText = reader.ReadToEnd();
                                        containerInfo.LoadXml(responseText);
                                    }
                                }
                            }
                            catch (System.Net.WebException e)
                            {
                                if (e.Status == WebExceptionStatus.ProtocolError && ((HttpWebResponse)e.Response).StatusCode == HttpStatusCode.NotFound)
                                {
                                    containerInfo = null;
                                }
                            }
                        }
                    } while (containerInfo != null);
                }
            }

            if (keepRunning)
            {
                WriteLog("Completed");
                PrintSummary();
            }
        }

        static void PrintSummary()
        {
            DateTime endTime = DateTime.Now;
            TimeSpan executionTime = endTime.Subtract(startTime);

            Console.WriteLine("");
            Console.WriteLine("************************************");
            Console.WriteLine("* Summary                          *");
            Console.WriteLine("************************************");
            Console.WriteLine("");
            Console.WriteLine("Total Time: " + ToReadableString(executionTime));
            Console.WriteLine("Files Transferred: " + intTransferred.ToString());
            Console.WriteLine("Files Skipped: " + intSkipped.ToString());
            Console.WriteLine("Bytes Transferred: " + String.Format(new FileSizeFormatProvider(), "File size: {0:fs}", bytesTransferred));
        }

        static void MyHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            Console.WriteLine("Unhandled Exception: " + e.Message);
        }

        static void WriteLog(string strMessage)
        {
            Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + strMessage);
        }

        static string ToReadableString(this TimeSpan span)
        {
            string formatted = string.Format("{0}{1}{2}{3}",
                span.Duration().Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? String.Empty : "s") : string.Empty,
                span.Duration().Hours > 0 ? string.Format("{0:0} hour{1}, ", span.Hours, span.Hours == 1 ? String.Empty : "s") : string.Empty,
                span.Duration().Minutes > 0 ? string.Format("{0:0} minute{1}, ", span.Minutes, span.Minutes == 1 ? String.Empty : "s") : string.Empty,
                span.Duration().Seconds > 0 ? string.Format("{0:0} second{1}", span.Seconds, span.Seconds == 1 ? String.Empty : "s") : string.Empty);

            if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

            if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

            return formatted;
        }

        #region http://blogs.microsoft.co.il/blogs/mneiter/archive/2009/03/22/how-to-encoding-and-decoding-base64-strings-in-c.aspx

        /// <summary>
        /// The method create a Base64 encoded string from a normal string.
        /// </summary>
        /// <param name="toEncode">The String containing the characters to encode.</param>
        /// <returns>The Base64 encoded string.</returns>
        public static string EncodeTo64(string toEncode)
        {

            byte[] toEncodeAsBytes

                  = System.Text.Encoding.Unicode.GetBytes(toEncode);

            string returnValue

                  = System.Convert.ToBase64String(toEncodeAsBytes);

            return returnValue;

        }

        /// <summary>
        /// The method to Decode your Base64 strings.
        /// </summary>
        /// <param name="encodedData">The String containing the characters to decode.</param>
        /// <returns>A String containing the results of decoding the specified sequence of bytes.</returns>
        public static string DecodeFrom64(string encodedData)
        {

            byte[] encodedDataAsBytes

                = System.Convert.FromBase64String(encodedData);

            string returnValue =

               System.Text.Encoding.Unicode.GetString(encodedDataAsBytes);

            return returnValue;

        }

        #endregion

    }

    public class FileSizeFormatProvider : IFormatProvider, ICustomFormatter
    {
        public object GetFormat(Type formatType)
        {
            if (formatType == typeof(ICustomFormatter)) return this;
            return null;
        }

        private const string fileSizeFormat = "fs";
        private const Decimal OneKiloByte = 1024M;
        private const Decimal OneMegaByte = OneKiloByte * 1024M;
        private const Decimal OneGigaByte = OneMegaByte * 1024M;

        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (format == null || !format.StartsWith(fileSizeFormat))
            {
                return defaultFormat(format, arg, formatProvider);
            }

            if (arg is string)
            {
                return defaultFormat(format, arg, formatProvider);
            }

            Decimal size;

            try
            {
                size = Convert.ToDecimal(arg);
            }
            catch (InvalidCastException)
            {
                return defaultFormat(format, arg, formatProvider);
            }

            string suffix;
            if (size > OneGigaByte)
            {
                size /= OneGigaByte;
                suffix = "GB";
            }
            else if (size > OneMegaByte)
            {
                size /= OneMegaByte;
                suffix = "MB";
            }
            else if (size > OneKiloByte)
            {
                size /= OneKiloByte;
                suffix = "kB";
            }
            else
            {
                suffix = " B";
            }

            string precision = format.Substring(2);
            if (String.IsNullOrEmpty(precision)) precision = "2";
            return String.Format("{0:N" + precision + "}{1}", size, suffix);

        }

        private static string defaultFormat(string format, object arg, IFormatProvider formatProvider)
        {
            IFormattable formattableArg = arg as IFormattable;
            if (formattableArg != null)
            {
                return formattableArg.ToString(format, formatProvider);
            }
            return arg.ToString();
        }

    }
}
