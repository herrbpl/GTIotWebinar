# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

<#

.SYNOPSIS
Self-signed device certificate generator.

.DESCRIPTION
Generates an X509 test certificate with the specified Common Name (CN).

.\GenerateTestCertificate.ps1 <DeviceID>

.EXAMPLE
.\GenerateTestCertificate.ps1 testdevice1

.NOTES


.LINK
https://github.com/azure/azure-iot-sdk-csharp

#>

Param(
	$deviceName = "iothubx509device1"
)

$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=$deviceName, O=TEST, C=US" -KeySpec Signature -KeyExportPolicy Exportable -HashAlgorithm sha256 -KeyLength 2048 -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.2") -CertStoreLocation "Cert:\CurrentUser\My"

Write-Host "Generated the certificate:"
Write-Host $cert

$tb = $cert.Thumbprint

$cmd = "dotnet IotDevice.dll --configfile ${deviceName}.json --thumbprint ${tb} --scopeid 0ne00018394"

set-content -path "${deviceName}.cmd" -Value $cmd -Encoding Ascii

Write-Host "Enter the PFX password:"
$password = Read-Host -AsSecureString

$cert | Export-PfxCertificate -FilePath "${deviceName}.pfx" -Password $password
Set-Content -Path "${deviceName}.cer" -Value ([Convert]::ToBase64String($cert.RawData)) -Encoding Ascii
