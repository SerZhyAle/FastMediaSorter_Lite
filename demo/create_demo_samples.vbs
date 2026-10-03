' VBScript to create demo sample files for T09 resource consumer
' This creates sample .fmscfg files and a compressed QR format file for testing

Option Explicit

Dim fso, androidFile, companionFile, qrFile
Set fso = CreateObject("Scripting.FileSystemObject")

' Create demo directory if it doesn't exist
If Not fso.FolderExists("demo") Then
    fso.CreateFolder "demo"
End If

' Android sample - plain JSON
Set androidFile = fso.CreateTextFile("demo\android_sample.fmscfg", True)
androidFile.WriteLine "{"
androidFile.WriteLine "  \"schemaVersion\": 2,"
androidFile.WriteLine "  \"protocol\": \"sftp\","
androidFile.WriteLine "  \"resourceName\": \"Android Phone Media\","
androidFile.WriteLine "  \"username\": \"user\","
androidFile.WriteLine "  \"password\": \"sample_password_123\","
androidFile.WriteLine "  \"accessNote\": \"Media from Android phone\","
androidFile.WriteLine "  \"hostKeyFingerprintSha256\": \"SHA256:1234567890abcdef1234567890abcdef1234567890abcdef1234567890ab\","
androidFile.WriteLine "  \"accessPaths\": ["
androidFile.WriteLine "    {"
androidFile.WriteLine "      \"kind\": \"lan\","
androidFile.WriteLine "      \"host\": \"192.168.1.100\","
androidFile.WriteLine "      \"port\": 22"
androidFile.WriteLine "    },"
androidFile.WriteLine "    {"
androidFile.WriteLine "      \"kind\": \"ipv6\","
androidFile.WriteLine "      \"host\": \"2001:db8::1\","
androidFile.WriteLine "      \"port\": 22"
androidFile.WriteLine "    }"
androidFile.WriteLine "  ],"
androidFile.WriteLine "  \"roots\": ["
androidFile.WriteLine "    {"
androidFile.WriteLine "      \"virtualPath\": \"/DCIM/Camera\","
androidFile.WriteLine "      \"label\": \"Camera Photos\","
androidFile.WriteLine "      \"readOnly\": false,"
androidFile.WriteLine "      \"isDestination\": true,"
androidFile.WriteLine "      \"profile\": \"photo_storage\","
androidFile.WriteLine "      \"mediaTypes\": [\"image\", \"video\"],"
androidFile.WriteLine "      \"scanSubdirectories\": true,"
androidFile.WriteLine "      \"showSubfoldersAsItems\": false,"
androidFile.WriteLine "      \"showHiddenFiles\": false,"
androidFile.WriteLine "      \"allFiles\": false,"
androidFile.WriteLine "      \"destinationColor\": 4286946304,"
androidFile.WriteLine "      \"slideshowInterval\": 5,"
androidFile.WriteLine "      \"comment\": \"Photos from camera\""
androidFile.WriteLine "    },"
androidFile.WriteLine "    {"
androidFile.WriteLine "      \"virtualPath\": \"/Download/Media\","
androidFile.WriteLine "      \"label\": \"Downloaded Media\","
androidFile.WriteLine "      \"readOnly\": true,"
androidFile.WriteLine "      \"isDestination\": false,"
androidFile.WriteLine "      \"profile\": \"video_library\","
androidFile.WriteLine "      \"mediaTypes\": [\"video\", \"audio\"],"
androidFile.WriteLine "      \"scanSubdirectories\": true,"
androidFile.WriteLine "      \"comment\": \"Media downloads\""
androidFile.WriteLine "    }"
androidFile.WriteLine "  ]"
androidFile.WriteLine "}"
androidFile.Close

' Companion sample - plain JSON
Set companionFile = fso.CreateTextFile("demo\companion_sample.fmscfg", True)
companionFile.WriteLine "{"
companionFile.WriteLine "  \"schemaVersion\": 2,"
companionFile.WriteLine "  \"protocol\": \"sftp\","
companionFile.WriteLine "  \"resourceName\": \"FastMediaSorter Companion\","
companionFile.WriteLine "  \"username\": \"fmsuser\","
companionFile.WriteLine "  \"password\": \"companion_password_456\","
companionFile.WriteLine "  \"accessNote\": \"Shared from FastMediaSorter Companion Server\","
companionFile.WriteLine "  \"hostKeyFingerprintSha256\": \"SHA256:9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba\","
companionFile.WriteLine "  \"accessPaths\": ["
companionFile.WriteLine "    {"
companionFile.WriteLine "      \"kind\": \"lan\","
companionFile.WriteLine "      \"host\": \"192.168.1.200\","
companionFile.WriteLine "      \"port\": 55259"
companionFile.WriteLine "    },"
companionFile.WriteLine "    {"
companionFile.WriteLine "      \"kind\": \"portforward\","
companionFile.WriteLine "      \"host\": \"example.com\","
companionFile.WriteLine "      \"port\": 443"
companionFile.WriteLine "    }"
companionFile.WriteLine "  ],"
companionFile.WriteLine "  \"roots\": ["
companionFile.WriteLine "    {"
companionFile.WriteLine "      \"virtualPath\": \"/Media/Photos\","
companionFile.WriteLine "      \"label\": \"Shared Photos\","
companionFile.WriteLine "      \"readOnly\": false,"
companionFile.WriteLine "      \"isDestination\": true,"
companionFile.WriteLine "      \"profile\": \"photo_storage\","
companionFile.WriteLine "      \"mediaTypes\": [\"image\", \"gif\"],"
companionFile.WriteLine "      \"scanSubdirectories\": true,"
companionFile.WriteLine "      \"showHiddenFiles\": false,"
companionFile.WriteLine "      \"allFiles\": false,"
companionFile.WriteLine "      \"comment\": \"Photos shared from Companion\""
companionFile.WriteLine "    },"
companionFile.WriteLine "    {"
companionFile.WriteLine "      \"virtualPath\": \"/Media/Videos\","
companionFile.WriteLine "      \"label\": \"Shared Videos\","
companionFile.WriteLine "      \"readOnly\": false,"
companionFile.WriteLine "      \"isDestination\": true,"
companionFile.WriteLine "      \"profile\": \"video_library\","
companionFile.WriteLine "      \"mediaTypes\": [\"video\"],"
companionFile.WriteLine "      \"scanSubdirectories\": true,"
companionFile.WriteLine "      \"comment\": \"Videos shared from Companion\""
companionFile.WriteLine "    },"
companionFile.WriteLine "    {"
companionFile.WriteLine "      \"virtualPath\": \"/Media/Archives\","
companionFile.WriteLine "      \"label\": \"Archives\","
companionFile.WriteLine "      \"readOnly\": true,"
companionFile.WriteLine "      \"isDestination\": false,"
companionFile.WriteLine "      \"profile\": \"all_files\","
companionFile.WriteLine "      \"mediaTypes\": [],"
companionFile.WriteLine "      \"scanSubdirectories\": false,"
companionFile.WriteLine "      \"comment\": \"Archive files - read only\""
companionFile.WriteLine "    }"
companionFile.WriteLine "  ]"
companionFile.WriteLine "}"
companionFile.Close

' QR format sample (compressed)
' This would need to be created using proper gzip compression and base64 encoding
' For demo purposes, we'll just create the JSON and note that QR format is FMSCFG1: + base64(gzip(json))
Set qrFile = fso.CreateTextFile("demo\qr_format_example.txt", True)
qrFile.WriteLine "QR Format: FMSCFG1:" & Chr(13) & Chr(10)
qrFile.WriteLine "Base64 encoded gzip compressed JSON would go here" & Chr(13) & Chr(10)
qrFile.WriteLine "Example QR payload: FMSCFG1:H4sIA... (truncated)" & Chr(13) & Chr(10)
qrFile.WriteLine "" & Chr(13) & Chr(10)
qrFile.WriteLine "This file explains the QR format structure:" & Chr(13) & Chr(10)
qrFile.WriteLine "1. Start with 'FMSCFG1:' prefix" & Chr(13) & Chr(10)
qrFile.WriteLine "2. Follow with base64 encoded gzip compressed JSON" & Chr(13) & Chr(10)
qrFile.WriteLine "3. The JSON is identical to the plain .fmscfg format" & Chr(13) & Chr(10)
qrFile.WriteLine "4. Whitespace is tolerated in scanned QR payloads" & Chr(13) & Chr(10)
qrFile.Close

WScript.Echo "Demo sample files created successfully:"
WScript.Echo "- demo\android_sample.fmscfg (Android phone export)"
WScript.Echo "- demo\companion_sample.fmscfg (Companion server export)"
WScript.Echo "- demo\qr_format_example.txt (QR format explanation)"
WScript.Echo ""
WScript.Echo "These files demonstrate the resource consumer functionality of T09."