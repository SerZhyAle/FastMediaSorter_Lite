@set manifestInternalType=internal
@set manifestValidationType=validation
@set manifestExternalType=external
@set manifestCustomerType=customer
@set manifestPlatformType=platform
@set manifestType=%1

@if not "%manifestType%" == "%manifestExternalType%" (
	@if not "%manifestType%" == "%manifestInternalType%" (
		@if not "%manifestType%" == "%manifestValidationType%" (
			if not "%manifestType%" == "%manifestCustomerType%" (
				if not "%manifestType%" == "%manifestPlatformType%" (
					@goto :error
				)	
			)
		)
	)		
)

@cd Driver
@echo Going to unRegister the %manifestType% manifest

@if "%manifestType%" == "%manifestInternalType%" (	
	@call UnRegisterExternalManifest.bat
	@call UnRegisterValidationManifest.bat
	@call UnRegisterInternalManifest.bat
	@call UnRegisterCustomerManifest.bat
	@call UnRegisterPlatformManifest.bat
	@call UnRegisterTelemetryManifest.bat
)

@if "%manifestType%" == "%manifestValidationType%" (	
	@call UnRegisterValidationManifest.bat
	@call UnRegisterInternalManifest.bat
	@call UnRegisterCustomerManifest.bat
	@call UnRegisterPlatformManifest.bat
)

@if "%manifestType%" == "%manifestPlatformType%" (
	@call UnRegisterCustomerManifest.bat
	@call UnRegisterPlatformManifest.bat
)

@if "%manifestType%" == "%manifestCustomerType%" (
	@call UnRegisterCustomerManifest.bat
)
	
@if "%manifestType%" == "%manifestExternalType%" call UnRegisterExternalManifest.bat

@if "%manifestType%" == "%manifestInternalType%" (
@cd ..\uCode
@call UnRegisterManifest.bat
)
@cd ..\

@exit /b 0

:error
@echo The manifest type was not passed as an argument
@echo write UnRegisterAllManifests.bat [internal/validation/external]