Option Strict On

' <summary>
' Strings of the single-instance hand-off (108 H01): the Share Manager that is running but did
' not answer a forwarded request, and the consent question for a folder another program asked
' to open.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddActivationStrings()

        Add("Не удалось определить, запущен ли Share Manager. Второй экземпляр не запускается.",
            "Could not determine whether Share Manager is running. A second copy is not started.",
            "Не вдалося визначити, чи запущено Share Manager. Другий екземпляр не запускається.",
            "Es konnte nicht festgestellt werden, ob Share Manager läuft. Eine zweite Instanz wird nicht gestartet.",
            "Impossibile determinare se Share Manager è in esecuzione. Non viene avviata una seconda istanza.",
            "No se pudo determinar si Share Manager está en ejecución. No se inicia una segunda instancia.",
            "Impossible de déterminer si Share Manager est lancé. Une seconde instance n'est pas démarrée.",
            "Não foi possível determinar se o Share Manager está em execução. Uma segunda instância não é iniciada.",
            "تعذّر تحديد ما إذا كان Share Manager قيد التشغيل. لن يتم تشغيل نسخة ثانية.",
            "यह निर्धारित नहीं हो सका कि Share Manager चल रहा है या नहीं। दूसरा इंस्टेंस शुरू नहीं किया जाता।",
            "Share Manager চলছে কি না নির্ধারণ করা যায়নি। দ্বিতীয় কপি চালু করা হয় না।",
            "یہ معلوم نہیں ہو سکا کہ Share Manager چل رہا ہے یا نہیں۔ دوسری کاپی شروع نہیں کی جاتی۔",
            "无法确定 Share Manager 是否正在运行。不会启动第二个实例。")

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

        Add("Другая программа просит открыть папку на телефоне:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Телефоны с доступом к этому компьютеру смогут просматривать её файлы. Открыть папку?",
            "Another program is asking to open a folder on the phone:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Phones that can reach this computer will be able to browse its files. Open the folder?",
            "Інша програма просить відкрити теку на телефоні:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Телефони з доступом до цього комп'ютера зможуть переглядати її файли. Відкрити теку?",
            "Ein anderes Programm bittet darum, einen Ordner auf dem Telefon zu öffnen:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Telefone, die diesen Computer erreichen, können seine Dateien durchsuchen. Ordner öffnen?",
            "Un altro programma chiede di aprire una cartella sul telefono:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "I telefoni che raggiungono questo computer potranno sfogliarne i file. Aprire la cartella?",
            "Otro programa solicita abrir una carpeta en el teléfono:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Los teléfonos que lleguen a este equipo podrán explorar sus archivos. ¿Abrir la carpeta?",
            "Un autre programme demande d'ouvrir un dossier sur le téléphone :" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Les téléphones qui peuvent joindre cet ordinateur pourront parcourir ses fichiers. Ouvrir le dossier ?",
            "Outro programa está pedindo para abrir uma pasta no telefone:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "Os telefones que alcançarem este computador poderão navegar pelos arquivos dela. Abrir a pasta?",
            "يطلب برنامج آخر فتح مجلد على الهاتف:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "ستتمكن الهواتف التي تصل إلى هذا الكمبيوتر من تصفح ملفاته. هل تريد فتح المجلد؟",
            "कोई अन्य प्रोग्राम फ़ोन पर एक फ़ोल्डर खोलने का अनुरोध कर रहा है:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "इस कंप्यूटर तक पहुँचने वाले फ़ोन उसकी फ़ाइलें देख सकेंगे। फ़ोल्डर खोलें?",
            "অন্য একটি প্রোগ্রাম ফোনে একটি ফোল্ডার খুলতে অনুরোধ করছে:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "এই কম্পিউটারে পৌঁছাতে পারা ফোনগুলি এর ফাইল ব্রাউজ করতে পারবে। ফোল্ডারটি খুলবেন?",
            "ایک اور پروگرام فون پر ایک فولڈر کھولنے کی درخواست کر رہا ہے:" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "اس کمپیوٹر تک پہنچنے والے فون اس کی فائلیں دیکھ سکیں گے۔ فولڈر کھولیں؟",
            "另一个程序请求在手机上开放一个文件夹：" & vbCrLf & "{0}" & vbCrLf & vbCrLf &
            "能够连接到此电脑的手机将可以浏览其中的文件。要开放该文件夹吗？")

        Add("Проверить доступ снова",
            "Check access again",
            "Перевірити доступ знову",
            "Zugriff erneut prüfen",
            "Ricontrolla l'accesso",
            "Comprobar el acceso de nuevo",
            "Revérifier l'accès",
            "Verificar o acesso novamente",
            "إعادة فحص الوصول",
            "पहुँच दोबारा जाँचें",
            "অ্যাক্সেস আবার পরীক্ষা করুন",
            "رسائی دوبارہ جانچیں",
            "重新检查访问权限")

        Add("Проверка доступа не завершена - код и файл не выдаются. Нажмите «Проверить доступ снова».",
            "The access check did not finish - the code and the file are not handed out. Press «Check access again».",
            "Перевірку доступу не завершено - код і файл не видаються. Натисніть «Перевірити доступ знову».",
            "Die Zugriffsprüfung wurde nicht abgeschlossen - Code und Datei werden nicht herausgegeben. Klicken Sie auf «Zugriff erneut prüfen».",
            "Il controllo dell'accesso non è terminato: codice e file non vengono consegnati. Premi «Ricontrolla l'accesso».",
            "La comprobación de acceso no terminó: el código y el archivo no se entregan. Pulse «Comprobar el acceso de nuevo».",
            "La vérification de l'accès n'est pas terminée : le code et le fichier ne sont pas remis. Cliquez sur «Revérifier l'accès».",
            "A verificação de acesso não terminou: o código e o arquivo não são entregues. Clique em «Verificar o acesso novamente».",
            "لم يكتمل فحص الوصول - لا يتم تسليم الرمز والملف. اضغط «إعادة فحص الوصول».",
            "पहुँच की जाँच पूरी नहीं हुई - कोड और फ़ाइल नहीं दी जाएँगी। «पहुँच दोबारा जाँचें» दबाएँ।",
            "অ্যাক্সেস পরীক্ষা শেষ হয়নি - কোড ও ফাইল দেওয়া হচ্ছে না। «অ্যাক্সেস আবার পরীক্ষা করুন» চাপুন।",
            "رسائی کی جانچ مکمل نہیں ہوئی - کوڈ اور فائل نہیں دیے جاتے۔ «رسائی دوبارہ جانچیں» دبائیں۔",
            "访问检查未完成 - 不提供代码和文件。请点击「重新检查访问权限」。")

        Add("Эта файловая система не умеет ограничивать доступ к файлу, а в нём ключ доступа. Выберите диск с NTFS.",
            "This file system cannot restrict access to the file, and the file holds an access key. Pick an NTFS drive.",
            "Ця файлова система не вміє обмежувати доступ до файлу, а в ньому ключ доступу. Оберіть диск з NTFS.",
            "Dieses Dateisystem kann den Zugriff auf die Datei nicht beschränken, und die Datei enthält einen Zugangsschlüssel. Wählen Sie ein NTFS-Laufwerk.",
            "Questo file system non può limitare l'accesso al file, e il file contiene una chiave di accesso. Scegli un'unità NTFS.",
            "Este sistema de archivos no puede restringir el acceso al archivo, y el archivo contiene una clave de acceso. Elija una unidad NTFS.",
            "Ce système de fichiers ne peut pas restreindre l'accès au fichier, et celui-ci contient une clé d'accès. Choisissez un lecteur NTFS.",
            "Este sistema de arquivos não consegue restringir o acesso ao arquivo, e o arquivo contém uma chave de acesso. Escolha uma unidade NTFS.",
            "نظام الملفات هذا لا يستطيع تقييد الوصول إلى الملف، والملف يحتوي على مفتاح وصول. اختر محرك أقراص NTFS.",
            "यह फ़ाइल सिस्टम फ़ाइल तक पहुँच सीमित नहीं कर सकता, और फ़ाइल में एक एक्सेस कुंजी है। कोई NTFS ड्राइव चुनें।",
            "এই ফাইল সিস্টেম ফাইলের অ্যাক্সেস সীমিত করতে পারে না, আর ফাইলটিতে একটি অ্যাক্সেস কী আছে। একটি NTFS ড্রাইভ বেছে নিন।",
            "یہ فائل سسٹم فائل تک رسائی محدود نہیں کر سکتا، اور فائل میں رسائی کی کلید ہے۔ کوئی NTFS ڈرائیو منتخب کریں۔",
            "此文件系统无法限制对文件的访问，而该文件包含访问密钥。请选择 NTFS 驱动器。")

        Add("В эту папку могут писать и удалять из неё другие пользователи, а в файле ключ доступа. Выберите папку, доступную только вам.",
            "Other users can write to and delete from this folder, and the file holds an access key. Pick a folder only you can change.",
            "До цієї теки можуть писати й видаляти з неї інші користувачі, а у файлі ключ доступу. Оберіть теку, доступну лише вам.",
            "Andere Benutzer können in diesem Ordner schreiben und daraus löschen, und die Datei enthält einen Zugangsschlüssel. Wählen Sie einen Ordner, den nur Sie ändern können.",
            "Altri utenti possono scrivere in questa cartella ed eliminarne i contenuti, e il file contiene una chiave di accesso. Scegli una cartella modificabile solo da te.",
            "Otros usuarios pueden escribir en esta carpeta y eliminar de ella, y el archivo contiene una clave de acceso. Elija una carpeta que solo usted pueda modificar.",
            "D'autres utilisateurs peuvent écrire dans ce dossier et y supprimer des éléments, et le fichier contient une clé d'accès. Choisissez un dossier que vous seul pouvez modifier.",
            "Outros usuários podem gravar nesta pasta e excluir dela, e o arquivo contém uma chave de acesso. Escolha uma pasta que só você possa alterar.",
            "يمكن لمستخدمين آخرين الكتابة في هذا المجلد والحذف منه، والملف يحتوي على مفتاح وصول. اختر مجلدًا لا يستطيع تغييره سواك.",
            "अन्य उपयोगकर्ता इस फ़ोल्डर में लिख और इससे हटा सकते हैं, और फ़ाइल में एक एक्सेस कुंजी है। ऐसा फ़ोल्डर चुनें जिसे केवल आप बदल सकें।",
            "অন্য ব্যবহারকারীরা এই ফোল্ডারে লিখতে ও এখান থেকে মুছতে পারে, আর ফাইলটিতে একটি অ্যাক্সেস কী আছে। এমন একটি ফোল্ডার বেছে নিন যা শুধু আপনিই বদলাতে পারেন।",
            "دوسرے صارفین اس فولڈر میں لکھ سکتے اور اس سے حذف کر سکتے ہیں، اور فائل میں رسائی کی کلید ہے۔ ایسا فولڈر منتخب کریں جسے صرف آپ تبدیل کر سکیں۔",
            "其他用户可以在此文件夹中写入和删除内容，而该文件包含访问密钥。请选择只有您能修改的文件夹。")

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
