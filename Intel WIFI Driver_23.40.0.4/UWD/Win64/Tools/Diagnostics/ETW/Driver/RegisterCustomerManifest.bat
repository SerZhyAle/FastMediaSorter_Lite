@echo Going to register the driver's customer manifest

@call UnRegisterCustomerManifest.bat

@xcopy ..\..\RESOURCES\IntelWiFiDriverEtw_driver_customer.dll %windir%\system32\drivers\ /Y /I
@wevtutil im WlanDriverEventsCustomer.man /rf:%windir%\system32\drivers\IntelWiFiDriverEtw_driver_customer.dll /mf:%windir%\system32\drivers\IntelWiFiDriverEtw_driver_customer.dll

@echo The driver's customer manifest was registered