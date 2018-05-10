using System;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.IO;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Devices.Provisioning.Client;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CommandLine;
using CommandLine.Text;

namespace IotDevice
{

    class Options
    {

        [Option(Required = false, HelpText = "Thumbprint of certificate")]
        public string thumbPrint { get; set; }

        [Option(Default = "deviceConfig.json", Required = false, HelpText = "configFile")]
        public string configFile { get; set; }

        [Option(Required = false, HelpText = "DPS Scope ID")]
        public string scopeId { get; set; }
        
    }

    class Program
    {

       
        static DeviceClient deviceClient = null;
        static TwinCollection deviceTwinProperties = null;
        
        // Device Config file
        static string configFile = "deviceConfig.json";

        // Device Settings
        static string thumbPrint = String.Empty;
        static string dpsScopeId = String.Empty;
        static string deviceId = String.Empty;
        static string deviceIotHub = String.Empty;
        static X509Certificate2 deviceCert = null;

        // helpers
        static DeviceIdentity deviceIdentity = new DeviceIdentity();
        static bool registerDevice = false;

        static void Main(string[] args)
        {
            

            
            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(opts => {
                Console.WriteLine("Arguments: {0}", CommandLine.Parser.Default.FormatCommandLine(opts));

                // set variables
                configFile = opts.configFile;
                thumbPrint = opts.thumbPrint;
                dpsScopeId = opts.scopeId;
                

                // Connect to device hub
                InitDevice();
                Console.WriteLine("Device initiated");
                SendDeviceToCloudMessagesAsync();
                Console.WriteLine("Messages starting");

            })
            .WithNotParsed<Options>((errs) => {
                Console.WriteLine("Errors!");
            });
            
            Console.ReadLine();
        }



        // init device. Create device config if does not exist. 
        // If device config does not exist, device id is not known, so we attempt to read device.cer to determine it.
        // If device.cer is not found, we try to create it with new guid
        // or should we then look device id up by thumbprint?
        private static async void InitDevice()
        {                                   
            
            JObject jobj = LoadConfig(configFile);
            if (jobj == null || registerDevice)
            {
                jobj = RegisterDevice();
                SaveConfig(jobj);
            }
                                
            // Create device and connect
            IAuthenticationMethod auth = new DeviceAuthenticationWithX509Certificate(deviceId, deviceCert);
            deviceClient = DeviceClient.Create(deviceIotHub, auth);            
            
            try
            {
                await deviceClient.OpenAsync();
                await deviceClient.UpdateReportedPropertiesAsync(deviceTwinProperties);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            
        }

        private static JObject RegisterDevice()
        {
            if (string.IsNullOrEmpty(thumbPrint))
            {
                throw new Exception("No thumbprint in config or given as parameter");
            }

            if (string.IsNullOrEmpty(dpsScopeId))
            {
                throw new Exception("DPS Scope ID not given");
            }

            if (deviceCert == null) deviceCert = deviceIdentity.GetCert(thumbPrint);
            DeviceRegistrationResult deviceRegisterResult = deviceIdentity.RegisterDevice(deviceCert, dpsScopeId).GetAwaiter().GetResult();
            if (deviceRegisterResult.Status != ProvisioningRegistrationStatusType.Assigned)
            {
                throw new Exception(String.Format("Device enrollment failed with code {0}", deviceRegisterResult.Status.ToString()));
            }
            registerDevice = false;

            deviceId = deviceRegisterResult.DeviceId;
            deviceIotHub = deviceRegisterResult.AssignedHub;

            if (deviceTwinProperties == null) deviceTwinProperties = new TwinCollection("{ samplingFrequency: 5, temperatureAlertThreshold: 35 }");

            return GetConfig(); 
        }

        private static JObject LoadConfig(string configFile)
        {
            if (!File.Exists(configFile)) { return null;  }

            JObject jobj;

            try
            {
                using (StreamReader r = new StreamReader(configFile))
                {
                    var json = r.ReadToEnd();
                    //var 
                    jobj = JObject.Parse(json);
                }
            } 
            catch (Exception Ex)
            {
                throw Ex;
            }

            // sanity checks for config
            // if global thumbprint has been set in argument, it takes precedence
            if (!string.IsNullOrEmpty(thumbPrint))
            {
                if (jobj["thumbPrint"] != null && !jobj["thumbPrint"].ToString().Equals(thumbPrint, StringComparison.OrdinalIgnoreCase)) {
                    throw new Exception("Certificate thumbprint mismatch!");
                }            
            } else
            {
                if (jobj["thumbPrint"] == null || string.IsNullOrWhiteSpace(jobj["thumbPrint"].ToString()))
                {
                    throw new Exception("No thumbprint in config or given as parameter");
                }
                thumbPrint = jobj["thumbPrint"].ToString();
            }

            

            // Check if IoT Hub exists, if not, register is needed.
            if (jobj.Property("iotHub") == null || string.IsNullOrWhiteSpace(jobj.Property("iotHub").Value.ToString()))
            {
                registerDevice = true;
            }
            else
            {
                deviceIotHub = jobj.Property("iotHub").Value.ToString();
            }

            // Load device cert if exists
            deviceCert = deviceIdentity.GetCert(thumbPrint);
            string deviceIdCert = deviceIdentity.DeviceIdFromCert(deviceCert);

            // Check if deviceID exists, if not, register is needed.
            if (jobj.Property("deviceId") == null || string.IsNullOrWhiteSpace(jobj.Property("deviceId").Value.ToString()))
            {                                
                if (string.IsNullOrWhiteSpace(deviceIdCert))
                {
                    registerDevice = true;
                }
                else
                {
                    deviceId = deviceIdCert;
                    if (jobj["deviceId"] != null) { jobj["deviceId"] = deviceId; } else { jobj.Add("deviceId", deviceId); }
                }
            }
            else
            {
                if (!deviceIdCert.Equals(jobj["deviceId"].ToString()))
                {
                    throw new Exception("deviceId mismatch!");
                }
                deviceId = jobj.Property("deviceId").Value.ToString();
            }

            if (jobj["deviceProperties"] != null)
            {
                deviceTwinProperties = new TwinCollection(jobj["deviceProperties"].ToString());                
            }

            return jobj;
        }

        // Main device reading loop

        private static async void SendDeviceToCloudMessagesAsync()
        {
            // Initial telemetry values
            double minTemperature = 20;
            double minHumidity = 60;
            Random rand = new Random();

            while (true)
            {
                double currentTemperature = minTemperature + rand.NextDouble() * 15;
                double currentHumidity = minHumidity + rand.NextDouble() * 20;

                // Create JSON message
                var telemetryDataPoint = new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity
                };
                var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                var message = new Message(Encoding.ASCII.GetBytes(messageString));

                // Add a custom application property to the message.
                // An IoT hub can filter on these properties without access to the message body.
                int temperatureAlertThreshold = deviceTwinProperties["temperatureAlertThreshold"];

                message.Properties.Add("temperatureAlert", (currentTemperature > temperatureAlertThreshold) ? "true" : "false");

                // Send the tlemetry message
                await deviceClient.SendEventAsync(message);
                Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);
                int samplingFrequency = deviceTwinProperties["samplingFrequency"];
                await Task.Delay(samplingFrequency * 1000);
            }
        }

        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                
                

                if ((desiredProperties != null)) 
                {
                    Console.WriteLine("\nInitiating config change");
                    if (desiredProperties["samplingFrequency"] != null && desiredProperties["samplingFrequency"] > 0) deviceTwinProperties["samplingFrequency"] = desiredProperties["samplingFrequency"];
                    if (desiredProperties["temperatureAlertThreshold"] != null && desiredProperties["temperatureAlertThreshold"] > 0) deviceTwinProperties["temperatureAlertThreshold"] = desiredProperties["temperatureAlertThreshold"];

                    SaveConfig(GetConfig());
                    await deviceClient.UpdateReportedPropertiesAsync(deviceTwinProperties);

                    
                }
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error in sample: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error in sample: {0}", ex.Message);
            }
        }

        private static JObject GetConfig()
        {
            JObject jobj = new JObject();
           

            jobj.Add("thumbPrint", thumbPrint);
            jobj.Add("deviceId", deviceId);
            jobj.Add("iotHub", deviceIotHub);
            
            jobj.Add("deviceProperties", JToken.FromObject(deviceTwinProperties));
            return jobj;
        }

        private static void SaveConfig(JObject jobj)
        {
            // save to config
            File.WriteAllText(configFile, jobj.ToString());
        }
    }
}
