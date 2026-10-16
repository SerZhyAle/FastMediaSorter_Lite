Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' DEVICE-EXCHANGE 1.1 section 2 item CW; imported terms, specification 233.
' The relay term is internal only. Google Drive uses words on single-tone controls (ICON-EXTERNAL rule 1).
Partial Public NotInheritable Class Localization
    Private Shared Sub AddExchangeVocabularyStrings()
#If NETFRAMEWORK Then
        Add("Трансляция", "Broadcast", "Трансляція", "Übertragung", "Trasmissione", "Transmisión", "Diffusion", "Transmissão", "بث", "प्रसारण", "সম্প্রচার", "نشریات", "广播")
        Add("через Google Drive", "through Google Drive", "через Google Drive", "über Google Drive", "tramite Google Drive", "a través de Google Drive", "via Google Drive", "pelo Google Drive", "عبر Google Drive", "Google Drive के माध्यम से", "Google Drive-এর মাধ্যমে", "Google Drive کے ذریعے", "通过 Google Drive")
        Add("через GD", "through GD", "через GD", "über GD", "tramite GD", "a través de GD", "via GD", "pelo GD", "عبر GD", "GD के माध्यम से", "GD-এর মাধ্যমে", "GD کے ذریعے", "通过 GD")
#Else
        Add("Подключить устройство", "Connect a device", "Підключити пристрій", "Gerät verbinden", "Collega un dispositivo", "Conectar un dispositivo", "Connecter un appareil", "Ligar um dispositivo", "توصيل جهاز", "डिवाइस कनेक्ट करें", "ডিভাইস সংযোগ করুন", "آلہ منسلک کریں", "连接设备")
        Add("Трансляция", "Broadcast", "Трансляція", "Übertragung", "Trasmissione", "Transmisión", "Diffusion", "Transmissão", "بث", "प्रसारण", "সম্প্রচার", "نشریات", "广播")
        Add("через Google Drive", "via Google Drive", "через Google Drive", "über Google Drive", "tramite Google Drive", "a través de Google Drive", "via Google Drive", "pelo Google Drive", "عبر Google Drive", "Google Drive के माध्यम से", "Google Drive-এর মাধ্যমে", "Google Drive کے ذریعے", "通过 Google Drive")
        Add("через GD", "via GD", "через GD", "über GD", "tramite GD", "a través de GD", "via GD", "pelo GD", "عبر GD", "GD के माध्यम से", "GD-এর মাধ্যমে", "GD کے ذریعے", "通过 GD")
        Add("Отправить буфер на сервер", "Send the clipboard to the server", "Надіслати буфер на сервер", "Zwischenablage an den Server senden", "Invia gli appunti al server", "Enviar el portapapeles al servidor", "Envoyer le presse-papiers au serveur", "Enviar a área de transferência ao servidor", "إرسال الحافظة إلى الخادم", "क्लिपबोर्ड को सर्वर पर भेजें", "ক্লিপবোর্ড সার্ভারে পাঠান", "کلپ بورڈ سرور پر بھیجیں", "将剪贴板发送到服务器")
        Add("Получить буфер с сервера", "Get the clipboard from the server", "Отримати буфер із сервера", "Zwischenablage vom Server holen", "Ottieni gli appunti dal server", "Obtener el portapapeles del servidor", "Récupérer le presse-papiers du serveur", "Obter a área de transferência do servidor", "جلب الحافظة من الخادم", "सर्वर से क्लिपबोर्ड प्राप्त करें", "সার্ভার থেকে ক্লিপবোর্ড নিন", "سرور سے کلپ بورڈ حاصل کریں", "从服务器获取剪贴板")
        Add("Буфер на сервере", "Clipboard on the server", "Буфер на сервері", "Zwischenablage auf dem Server", "Appunti sul server", "Portapapeles en el servidor", "Presse-papiers sur le serveur", "Área de transferência no servidor", "الحافظة على الخادم", "सर्वर पर क्लिपबोर्ड", "সার্ভারে ক্লিপবোর্ড", "سرور پر کلپ بورڈ", "服务器上的剪贴板")
        Add("Буфер, полученный с сервера", "Clipboard received from the server", "Буфер, отриманий із сервера", "Zwischenablage vom Server empfangen", "Appunti ricevuti dal server", "Portapapeles recibido del servidor", "Presse-papiers reçu du serveur", "Área de transferência recebida do servidor", "تم استلام الحافظة من الخادم", "सर्वर से क्लिपबोर्ड प्राप्त हुआ", "সার্ভার থেকে ক্লিপবোর্ড পাওয়া গেছে", "سرور سے کلپ بورڈ موصول ہوا", "已从服务器接收剪贴板")
        Add("Гость", "Guest", "Гість", "Gast", "Ospite", "Invitado", "Invité", "Convidado", "ضيف", "अतिथि", "অতিথি", "مہمان", "访客")
        Add("Сервер обмена", "Exchange server", "Сервер обміну", "Austauschserver", "Server di scambio", "Servidor de intercambio", "Serveur d’échange", "Servidor de intercâmbio", "خادم التبادل", "एक्सचेंज सर्वर", "এক্সচেঞ্জ সার্ভার", "ایکسچینج سرور", "交换服务器")
        Add("Прямой доступ (сервис шаринга)", "Direct access (the sharing service)", "Прямий доступ (сервіс шарингу)", "Direkter Zugriff (Freigabedienst)", "Accesso diretto (servizio di condivisione)", "Acceso directo (servicio de uso compartido)", "Accès direct (service de partage)", "Acesso direto (serviço de partilha)", "الوصول المباشر (خدمة المشاركة)", "सीधी पहुँच (साझाकरण सेवा)", "সরাসরি প্রবেশ (শেয়ারিং পরিষেবা)", "براہ راست رسائی (شیئرنگ سروس)", "直接访问（共享服务）")
        Add("Вещание", "Broadcasting", "Мовлення", "Senden", "Trasmissione", "Emisión", "Diffusion", "Transmissão", "البث", "प्रसारण", "সম্প্রচার", "نشریات", "广播发送")
        Add("Вещание готового файла", "Broadcast of a ready file", "Мовлення готового файлу", "Sendung einer fertigen Datei", "Trasmissione di un file pronto", "Emisión de un archivo listo", "Diffusion d’un fichier prêt", "Transmissão de um ficheiro pronto", "بث ملف جاهز", "तैयार फ़ाइल का प्रसारण", "প্রস্তুত ফাইলের সম্প্রচার", "تیار فائل کی نشریات", "广播已有文件")
        Add("Раздача готового фильма", "Giving out a ready film", "Роздача готового фільму", "Bereitstellen eines fertigen Films", "Distribuzione di un film pronto", "Distribución de una película lista", "Mise à disposition d’un film prêt", "Distribuição de um filme pronto", "إتاحة فيلم جاهز", "तैयार फ़िल्म साझा करना", "প্রস্তুত চলচ্চিত্র বিতরণ", "تیار فلم کی تقسیم", "提供已有影片")
        Add("Cast на своё устройство", "Cast to your own device", "Cast на свій пристрій", "Auf eigenes Gerät übertragen", "Trasmetti al tuo dispositivo", "Transmitir a tu dispositivo", "Diffuser sur votre appareil", "Transmitir para o seu dispositivo", "الإرسال إلى جهازك", "अपने डिवाइस पर कास्ट करें", "নিজের ডিভাইসে কাস্ট করুন", "اپنے آلے پر کاسٹ کریں", "投放到自己的设备")
        Add("Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast", "Google Cast")
        Add("Буфер", "Clipboard", "Буфер", "Zwischenablage", "Appunti", "Portapapeles", "Presse-papiers", "Área de transferência", "الحافظة", "क्लिपबोर्ड", "ক্লিপবোর্ড", "کلپ بورڈ", "剪贴板")
        Add("Приглашение", "Invitation", "Запрошення", "Einladung", "Invito", "Invitación", "Invitation", "Convite", "دعوة", "निमंत्रण", "আমন্ত্রণ", "دعوت", "邀请")
        Add("Своё устройство или семья", "Own device or family", "Свій пристрій або сім'я", "Eigenes Gerät oder Familie", "Dispositivo proprio o famiglia", "Dispositivo propio o familia", "Appareil personnel ou famille", "Dispositivo próprio ou família", "جهازك أو عائلتك", "अपना डिवाइस या परिवार", "নিজের ডিভাইস বা পরিবার", "اپنا آلہ یا خاندان", "自己的设备或家庭")
        Add("Ссылка с паролем", "Link with a password", "Посилання з паролем", "Link mit Passwort", "Link con password", "Enlace con contraseña", "Lien avec mot de passe", "Ligação com palavra-passe", "رابط بكلمة مرور", "पासवर्ड वाला लिंक", "পাসওয়ার্ডসহ লিঙ্ক", "پاس ورڈ والا لنک", "带密码的链接")
#End If
    End Sub
End Class
