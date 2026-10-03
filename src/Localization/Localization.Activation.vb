Option Strict On

' <summary>
' Strings of the viewer's hand-off to the Share Manager (108 H01, Q108-1): the Share Manager that
' is running but did not answer, and the one that is too old to ask the user before it shares a
' folder it was sent.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddActivationStrings()

        Add("Share Manager уже запущен, но не ответил на запрос. Подождите немного и повторите; второй экземпляр не запускается.",
            "Share Manager is already running but did not answer the request. Wait a moment and try again; a second copy is not started.",
            "Share Manager уже запущено, але він не відповів на запит. Зачекайте трохи й повторіть; другий екземпляр не запускається.",
            "Share Manager läuft bereits, hat die Anfrage aber nicht beantwortet. Warten Sie einen Moment und versuchen Sie es erneut; eine zweite Instanz wird nicht gestartet.",
            "Share Manager è già in esecuzione ma non ha risposto alla richiesta. Attendi un momento e riprova; non viene avviata una seconda istanza.",
            "Share Manager ya se está ejecutando pero no respondió a la solicitud. Espere un momento y vuelva a intentarlo; no se inicia una segunda instancia.",
            "Share Manager est déjà lancé mais n'a pas répondu à la demande. Patientez un instant et réessayez ; une seconde instance n'est pas démarrée.",
            "O Share Manager já está em execução, mas não respondeu à solicitação. Aguarde um instante e tente novamente; uma segunda instância não é iniciada.",
            "Share Manager قيد التشغيل بالفعل لكنه لم يستجب للطلب. انتظر قليلاً ثم أعد المحاولة؛ لن يتم تشغيل نسخة ثانية.",
            "Share Manager पहले से चल रहा है, लेकिन उसने अनुरोध का उत्तर नहीं दिया। थोड़ी देर प्रतीक्षा करके दोबारा प्रयास करें; दूसरा इंस्टेंस शुरू नहीं किया जाता।",
            "Share Manager ইতিমধ্যে চলছে, কিন্তু অনুরোধের উত্তর দেয়নি। একটু অপেক্ষা করে আবার চেষ্টা করুন; দ্বিতীয় কপি চালু করা হয় না।",
            "Share Manager پہلے سے چل رہا ہے لیکن اس نے درخواست کا جواب نہیں دیا۔ تھوڑی دیر انتظار کریں اور دوبارہ کوشش کریں؛ دوسری کاپی شروع نہیں کی جاتی۔",
            "Share Manager 已在运行，但没有响应该请求。请稍候再试；不会启动第二个实例。")

        Add("Share Manager старой версии не умеет безопасно принимать папку отсюда. Обновите его или добавьте папку в нём вручную:" & vbCrLf & "{0}",
            "This Share Manager is an older version and cannot safely accept a folder from here. Update it, or add the folder in it yourself:" & vbCrLf & "{0}",
            "Share Manager старої версії не вміє безпечно приймати теку звідси. Оновіть його або додайте теку в ньому вручну:" & vbCrLf & "{0}",
            "Dieser Share Manager ist eine ältere Version und kann einen Ordner von hier nicht sicher entgegennehmen. Aktualisieren Sie ihn oder fügen Sie den Ordner dort selbst hinzu:" & vbCrLf & "{0}",
            "Questo Share Manager è una versione precedente e non può accettare in sicurezza una cartella da qui. Aggiornalo oppure aggiungi tu la cartella al suo interno:" & vbCrLf & "{0}",
            "Este Share Manager es una versión anterior y no puede aceptar una carpeta desde aquí de forma segura. Actualícelo o añada usted mismo la carpeta en él:" & vbCrLf & "{0}",
            "Ce Share Manager est une version plus ancienne et ne peut pas accepter un dossier d'ici en toute sécurité. Mettez-le à jour ou ajoutez vous-même le dossier dedans :" & vbCrLf & "{0}",
            "Este Share Manager é uma versão mais antiga e não pode aceitar uma pasta daqui com segurança. Atualize-o ou adicione a pasta nele você mesmo:" & vbCrLf & "{0}",
            "هذه نسخة أقدم من Share Manager ولا يمكنها قبول مجلد من هنا بأمان. حدّثها أو أضف المجلد فيها بنفسك:" & vbCrLf & "{0}",
            "यह Share Manager का पुराना संस्करण है और यहाँ से फ़ोल्डर को सुरक्षित रूप से स्वीकार नहीं कर सकता। इसे अपडेट करें, या फ़ोल्डर को उसमें स्वयं जोड़ें:" & vbCrLf & "{0}",
            "এটি Share Manager-এর একটি পুরনো সংস্করণ এবং এখান থেকে নিরাপদে ফোল্ডার গ্রহণ করতে পারে না। এটি আপডেট করুন, অথবা ফোল্ডারটি নিজে এতে যোগ করুন:" & vbCrLf & "{0}",
            "یہ Share Manager کا پرانا ورژن ہے اور یہاں سے فولڈر کو محفوظ طریقے سے قبول نہیں کر سکتا۔ اسے اپ ڈیٹ کریں، یا فولڈر خود اس میں شامل کریں:" & vbCrLf & "{0}",
            "此 Share Manager 版本较旧，无法安全地接收来自这里的文件夹。请更新它，或自行在其中添加该文件夹：" & vbCrLf & "{0}")

    End Sub

End Class
