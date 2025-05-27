::Note this file is consumed by PublishAllOutputs.py and expected to work only if properly located at layout

@pushd ETW
@call UnRegisterAllManifests.bat customer
@popd

@pushd PCW\Driver
@call UnRegisterCustomerManifest.bat
@popd