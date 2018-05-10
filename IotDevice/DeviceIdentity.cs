using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;


namespace IotDevice
{
    class DeviceIdentity
    {

        private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";        

        public X509Certificate2 GetCert(string thumbPrint)
        {
            try
            {
                using (X509Store store = new X509Store("my", StoreLocation.CurrentUser))
                {
                    store.Open(OpenFlags.ReadOnly);
                    foreach (X509Certificate2 cert in store.Certificates)
                    {
                        if (cert.Thumbprint.Equals(thumbPrint, StringComparison.OrdinalIgnoreCase))
                        {
                            if (cert.HasPrivateKey)
                            {
                                return cert;
                            }
                            else
                            {
                                throw new Exception(String.Format("Certificate with Thumbprint {0} is missing private key", thumbPrint));
                            }
                        }
                    }
                }
            } catch (Exception e)
            {
                throw e;
            }
            throw new Exception(String.Format("Certificate with Thumbprint {0} not found!", thumbPrint));
        }
               
        public string DeviceIdFromCert(X509Certificate2 cert)
        {
            try
            {
                return cert.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (Exception e)
            {
                throw e;
            }            
        }

        /// <summary>
        /// Registers device with IoT Hub using Device Provisioning Service
        /// </summary>
        /// <param name="cert"></param>
        /// <param name="s_idScope"></param>
        /// <returns></returns>
        public async Task<DeviceRegistrationResult> RegisterDevice(X509Certificate2 cert, string s_idScope)
        {
            using (var security = new SecurityProviderX509Certificate(cert))
            // using (var transport = new ProvisioningTransportHandlerHttp())
            using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
            // using (var transport = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpOnly))
            {
                ProvisioningDeviceClient provClient =
                    ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, s_idScope, security, transport);
                DeviceRegistrationResult result = await provClient.RegisterAsync();
                return result;
            }

        }

    }
}
