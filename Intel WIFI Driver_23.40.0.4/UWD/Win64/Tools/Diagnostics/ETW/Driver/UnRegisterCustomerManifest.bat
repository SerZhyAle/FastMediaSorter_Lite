@echo Going to unregister the driver's customer manifest

@wevtutil um WlanDriverEventsCustomer.man
@del /F /Q %windir%\system32\drivers\IntelWiFiDriverEtw_driver_customer.dll

@echo The driver's customer manifest was unregistered
