@set manifestInternalType=internal
@set manifestValidationType=validation
@set manifestExternalType=external
@set manifestCustomerType=customer
@set manifestPlatformType=platform
@set manifestType=%1

@if not "%manifestType%" == "%manifestExternalType%" (
	@if not "%manifestType%" == "%manifestInternalType%" (
		@if not "%manifestType%" == "%manifestValidationType%" (
			@if not "%manifestType%" == "%manifestCustomerType%" (
				@if not "%manifestType%" == "%manifestPlatformType%" (
					@echo error: 
				)
			)
		)
	)		
)

@pushd Driver
@echo Going to register the %manifestType% manifests
@if "%manifestType%" == "%manifestInternalType%" (
	@call RegisterExternalManifest.bat
	@call RegisterValidationManifest.bat
	@call RegisterInternalManifest.bat
	@call RegisterCustomerManifest.bat
	@call RegisterPlatformManifest.bat
	@call RegisterTelemetryManifest.bat
)

@if "%manifestType%" == "%manifestExternalType%" (
	@call RegisterExternalManifest.bat
	@call RegisterCustomerManifest.bat
	@call RegisterPlatformManifest.bat
)

@if "%manifestType%" == "%manifestValidationType%" (
	@call RegisterValidationManifest.bat
	@call RegisterInternalManifest.bat
	@call RegisterCustomerManifest.bat
	@call RegisterPlatformManifest.bat
)

@if "%manifestType%" == "%manifestPlatformType%" (
	@call RegisterCustomerManifest.bat
	@call RegisterPlatformManifest.bat
)

@if "%manifestType%" == "%manifestCustomerType%" (
	@call RegisterCustomerManifest.bat
)

@if "%manifestType%" == "%manifestInternalType%" (
	@cd ..\uCode
	@call RegisterManifest.bat
)
@popd

@exit /b 0

:error
@echo The manifest type was not passed as an argument
@echo write RegisterAllManifests.bat [internal/validation/external/customer/platform]