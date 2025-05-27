::Note this file is consumed by PublishAllOutputs.py and expected to work only if properly located at layout

@pushd ETW
@call RegisterAllManifests.bat customer
@popd

@pushd PCW\Driver
@call RegisterCustomerManifest.bat
@popd
