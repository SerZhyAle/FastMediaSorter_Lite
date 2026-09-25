Option Strict On

' <summary>
' Strings of 028_SPECIFICATION_CONTRACT_SYNC_DESKTOP_APP_UX.md on the Share Manager's side:
' confirmations that name what stops working (Ф-5), failures with an action and the
' Share Manager's own "send the log" entry (Ф-3), the fuller consent and its withdrawal
' (Ф-6) and the accessible names of glyph-only controls (Ф-7).
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddAppUxStrings()

        ' --- Ф-5: confirmations ----------------------------------------------------
        Add("Убрать папку «{0}» из раздачи?" & vbCrLf & vbCrLf &
            "Телефоны перестанут её видеть, а её настройки (PIN, фильтры, папка назначения) будут удалены.",
            "Remove the folder «{0}» from sharing?" & vbCrLf & vbCrLf &
            "Phones will no longer see it, and its settings (PIN, filters, destination folder) will be deleted.",
            "Прибрати теку «{0}» з роздачі?" & vbCrLf & vbCrLf &
            "Телефони перестануть її бачити, а її налаштування (PIN, фільтри, тека призначення) буде видалено.",
            "Den Ordner «{0}» aus der Freigabe entfernen?" & vbCrLf & vbCrLf &
            "Telefone sehen ihn dann nicht mehr, und seine Einstellungen (PIN, Filter, Zielordner) werden gelöscht.",
            "Rimuovere la cartella «{0}» dalla condivisione?" & vbCrLf & vbCrLf &
            "I telefoni non la vedranno più e le sue impostazioni (PIN, filtri, cartella di destinazione) verranno eliminate.",
            "¿Quitar la carpeta «{0}» del uso compartido?" & vbCrLf & vbCrLf &
            "Los teléfonos dejarán de verla y se eliminarán sus ajustes (PIN, filtros, carpeta de destino).",
            "Retirer le dossier «{0}» du partage ?" & vbCrLf & vbCrLf &
            "Les téléphones ne le verront plus et ses réglages (PIN, filtres, dossier de destination) seront supprimés.",
            "Remover a pasta «{0}» do compartilhamento?" & vbCrLf & vbCrLf &
            "Os telefones deixarão de vê-la e as configurações dela (PIN, filtros, pasta de destino) serão excluídas.",
            "إزالة المجلد «{0}» من المشاركة؟" & vbCrLf & vbCrLf &
            "لن تراه الهواتف بعد الآن، وستُحذف إعداداته (رمز PIN، والمرشحات، ومجلد الوجهة).",
            "फ़ोल्डर «{0}» को साझा करने से हटाएँ?" & vbCrLf & vbCrLf &
            "फ़ोन इसे अब नहीं देख पाएँगे, और इसकी सेटिंग्स (PIN, फ़िल्टर, गंतव्य फ़ोल्डर) हटा दी जाएँगी।",
            "ফোল্ডার «{0}» শেয়ার থেকে সরাবেন?" & vbCrLf & vbCrLf &
            "ফোনগুলি আর এটি দেখতে পাবে না, এবং এর সেটিংস (PIN, ফিল্টার, গন্তব্য ফোল্ডার) মুছে যাবে।",
            "فولڈر «{0}» کو شیئرنگ سے ہٹائیں؟" & vbCrLf & vbCrLf &
            "فون اسے مزید نہیں دیکھ سکیں گے، اور اس کی ترتیبات (PIN، فلٹر، منزل کا فولڈر) حذف ہو جائیں گی۔",
            "要将文件夹「{0}」从共享中移除吗？" & vbCrLf & vbCrLf &
            "手机将无法再看到它，其设置（PIN、筛选条件、目标文件夹）将被删除。")
        Add("Это последняя отмеченная папка: раздача остановится.",
            "This is the last ticked folder: sharing will stop.",
            "Це остання позначена тека: роздача зупиниться.",
            "Dies ist der letzte markierte Ordner: Die Freigabe wird beendet.",
            "Questa è l'ultima cartella selezionata: la condivisione si fermerà.",
            "Es la última carpeta marcada: el uso compartido se detendrá.",
            "C'est le dernier dossier coché : le partage s'arrêtera.",
            "Esta é a última pasta marcada: o compartilhamento será interrompido.",
            "هذا آخر مجلد محدد: ستتوقف المشاركة.",
            "यह अंतिम चुना हुआ फ़ोल्डर है: साझा करना रुक जाएगा।",
            "এটি শেষ চিহ্নিত ফোল্ডার: শেয়ার করা বন্ধ হয়ে যাবে।",
            "یہ آخری منتخب فولڈر ہے: شیئرنگ رک جائے گی۔",
            "这是最后一个勾选的文件夹：共享将停止。")
        Add("Это последняя отмеченная папка. Снять отметку и остановить раздачу? Подключённые телефоны будут отключены.",
            "This is the last ticked folder. Untick it and stop sharing? Connected phones will be disconnected.",
            "Це остання позначена тека. Зняти позначку та зупинити роздачу? Підключені телефони буде від'єднано.",
            "Dies ist der letzte markierte Ordner. Markierung entfernen und die Freigabe beenden? Verbundene Telefone werden getrennt.",
            "Questa è l'ultima cartella selezionata. Deselezionarla e interrompere la condivisione? I telefoni connessi verranno disconnessi.",
            "Es la última carpeta marcada. ¿Desmarcarla y detener el uso compartido? Los teléfonos conectados se desconectarán.",
            "C'est le dernier dossier coché. Le décocher et arrêter le partage ? Les téléphones connectés seront déconnectés.",
            "Esta é a última pasta marcada. Desmarcá-la e interromper o compartilhamento? Os telefones conectados serão desconectados.",
            "هذا آخر مجلد محدد. هل تريد إلغاء تحديده وإيقاف المشاركة؟ سيتم قطع اتصال الهواتف المتصلة.",
            "यह अंतिम चुना हुआ फ़ोल्डर है। चयन हटाकर साझा करना बंद करें? जुड़े हुए फ़ोन डिस्कनेक्ट हो जाएँगे।",
            "এটি শেষ চিহ্নিত ফোল্ডার। চিহ্ন তুলে শেয়ার করা বন্ধ করবেন? সংযুক্ত ফোনগুলি বিচ্ছিন্ন হয়ে যাবে।",
            "یہ آخری منتخب فولڈر ہے۔ نشان ہٹا کر شیئرنگ روک دیں؟ جڑے ہوئے فون منقطع ہو جائیں گے۔",
            "这是最后一个勾选的文件夹。取消勾选并停止共享吗？已连接的手机将被断开。")
        Add("Обнулить счётчики подключений и файлов? Прежние значения восстановить не получится.",
            "Reset the connection and file counters? The previous values cannot be restored.",
            "Обнулити лічильники підключень і файлів? Попередні значення відновити не вийде.",
            "Verbindungs- und Dateizähler zurücksetzen? Die bisherigen Werte lassen sich nicht wiederherstellen.",
            "Azzerare i contatori di connessioni e file? I valori precedenti non potranno essere ripristinati.",
            "¿Poner a cero los contadores de conexiones y archivos? Los valores anteriores no se podrán recuperar.",
            "Remettre à zéro les compteurs de connexions et de fichiers ? Les valeurs précédentes ne pourront pas être restaurées.",
            "Zerar os contadores de conexões e arquivos? Os valores anteriores não poderão ser restaurados.",
            "هل تريد تصفير عدادات الاتصالات والملفات؟ لا يمكن استعادة القيم السابقة.",
            "कनेक्शन और फ़ाइल काउंटर शून्य करें? पिछले मान वापस नहीं लाए जा सकेंगे।",
            "সংযোগ ও ফাইলের কাউন্টার শূন্য করবেন? আগের মানগুলি ফিরিয়ে আনা যাবে না।",
            "کنکشن اور فائل کاؤنٹر صفر کریں؟ پچھلی قدریں بحال نہیں ہو سکیں گی۔",
            "要清零连接和文件计数器吗？之前的数值将无法恢复。")
        Add("Удалить роль сервера?",
            "Remove the server role?",
            "Видалити роль сервера?",
            "Die Serverrolle entfernen?",
            "Rimuovere il ruolo di server?",
            "¿Quitar el rol de servidor?",
            "Supprimer le rôle de serveur ?",
            "Remover a função de servidor?",
            "إزالة دور الخادم؟",
            "सर्वर भूमिका हटाएँ?",
            "সার্ভার ভূমিকা সরাবেন?",
            "سرور کا کردار ہٹائیں؟",
            "要移除服务器角色吗？")
        Add("Применить порт",
            "Apply port",
            "Застосувати порт",
            "Port übernehmen",
            "Applica porta",
            "Aplicar puerto",
            "Appliquer le port",
            "Aplicar porta",
            "تطبيق المنفذ",
            "पोर्ट लागू करें",
            "পোর্ট প্রয়োগ করুন",
            "پورٹ لاگو کریں",
            "应用端口")
        Add("Порт {0} ещё не применён - нажмите «Применить порт».",
            "Port {0} is not applied yet - press «Apply port».",
            "Порт {0} ще не застосовано - натисніть «Застосувати порт».",
            "Port {0} ist noch nicht übernommen - klicken Sie auf «Port übernehmen».",
            "La porta {0} non è ancora applicata: premi «Applica porta».",
            "El puerto {0} aún no se ha aplicado: pulse «Aplicar puerto».",
            "Le port {0} n'est pas encore appliqué : cliquez sur «Appliquer le port».",
            "A porta {0} ainda não foi aplicada - clique em «Aplicar porta».",
            "لم يُطبَّق المنفذ {0} بعد - اضغط «تطبيق المنفذ».",
            "पोर्ट {0} अभी लागू नहीं हुआ है - «पोर्ट लागू करें» दबाएँ।",
            "পোর্ট {0} এখনও প্রয়োগ হয়নি - «পোর্ট প্রয়োগ করুন» চাপুন।",
            "پورٹ {0} ابھی لاگو نہیں ہوا - «پورٹ لاگو کریں» دبائیں۔",
            "端口 {0} 尚未应用，请点击「应用端口」。")
        Add("Сменить порт {0} на {1}?" & vbCrLf & vbCrLf &
            "Все QR-коды и файлы .fmscfg, уже выданные телефонам, и правило проброса порта на роутере перестанут работать - их придётся создать заново. Работающая раздача перезапустится на новом порту.",
            "Change the port from {0} to {1}?" & vbCrLf & vbCrLf &
            "Every QR code and .fmscfg file already given to phones, and the port-forwarding rule on the router, will stop working - they will have to be made again. Running sharing restarts on the new port.",
            "Змінити порт {0} на {1}?" & vbCrLf & vbCrLf &
            "Усі QR-коди та файли .fmscfg, уже видані телефонам, і правило переадресації порту на роутері перестануть працювати - їх доведеться створити заново. Роздача, що працює, перезапуститься на новому порту.",
            "Port {0} in {1} ändern?" & vbCrLf & vbCrLf &
            "Alle QR-Codes und .fmscfg-Dateien, die Telefone bereits haben, sowie die Portweiterleitung im Router funktionieren dann nicht mehr - sie müssen neu erstellt werden. Eine laufende Freigabe startet auf dem neuen Port neu.",
            "Cambiare la porta da {0} a {1}?" & vbCrLf & vbCrLf &
            "Tutti i codici QR e i file .fmscfg già consegnati ai telefoni, e la regola di inoltro porta sul router, smetteranno di funzionare: dovranno essere ricreati. La condivisione in corso si riavvierà sulla nuova porta.",
            "¿Cambiar el puerto {0} por {1}?" & vbCrLf & vbCrLf &
            "Todos los códigos QR y archivos .fmscfg ya entregados a los teléfonos, y la regla de reenvío de puertos del router, dejarán de funcionar: habrá que crearlos de nuevo. El uso compartido en curso se reiniciará en el nuevo puerto.",
            "Changer le port {0} en {1} ?" & vbCrLf & vbCrLf &
            "Tous les codes QR et fichiers .fmscfg déjà remis aux téléphones, ainsi que la règle de redirection de port du routeur, cesseront de fonctionner : il faudra les recréer. Le partage en cours redémarrera sur le nouveau port.",
            "Mudar a porta {0} para {1}?" & vbCrLf & vbCrLf &
            "Todos os códigos QR e arquivos .fmscfg já entregues aos telefones, e a regra de redirecionamento de porta no roteador, deixarão de funcionar - será preciso criá-los de novo. O compartilhamento em andamento será reiniciado na nova porta.",
            "تغيير المنفذ {0} إلى {1}؟" & vbCrLf & vbCrLf &
            "ستتوقف جميع رموز QR وملفات ‎.fmscfg‎ التي سبق تسليمها إلى الهواتف، وقاعدة إعادة توجيه المنفذ على الموجّه، عن العمل - وسيتعين إنشاؤها من جديد. ستُعاد المشاركة الجارية على المنفذ الجديد.",
            "पोर्ट {0} को {1} में बदलें?" & vbCrLf & vbCrLf &
            "फ़ोन को पहले दिए गए सभी QR कोड और .fmscfg फ़ाइलें, और राउटर पर पोर्ट फ़ॉरवर्डिंग नियम, काम करना बंद कर देंगे - उन्हें फिर से बनाना होगा। चल रहा साझाकरण नए पोर्ट पर फिर से शुरू होगा।",
            "পোর্ট {0} থেকে {1}-এ বদলাবেন?" & vbCrLf & vbCrLf &
            "ফোনকে আগে দেওয়া সব QR কোড ও .fmscfg ফাইল, এবং রাউটারের পোর্ট ফরওয়ার্ডিং নিয়ম, আর কাজ করবে না - সেগুলি আবার তৈরি করতে হবে। চলমান শেয়ার নতুন পোর্টে আবার শুরু হবে।",
            "پورٹ {0} کو {1} میں بدلیں؟" & vbCrLf & vbCrLf &
            "فون کو پہلے دیے گئے تمام QR کوڈ اور ‎.fmscfg‎ فائلیں، اور راؤٹر پر پورٹ فارورڈنگ کا اصول، کام کرنا چھوڑ دیں گے - انہیں دوبارہ بنانا ہوگا۔ جاری شیئرنگ نئے پورٹ پر دوبارہ شروع ہوگی۔",
            "要将端口从 {0} 改为 {1} 吗？" & vbCrLf & vbCrLf &
            "已发给手机的所有二维码和 .fmscfg 文件，以及路由器上的端口转发规则，都将失效，需要重新创建。正在运行的共享将在新端口上重新启动。")


        ' --- Ф-3: the Share Manager's own "send the log" and failures with an action ---
        Add("Отправить журнал автору..",
            "Send the log to the author..", "Надіслати журнал авторові..", "Protokoll an den Autor senden..",
            "Invia il registro all'autore..", "Enviar el registro al autor..", "Envoyer le journal à l'auteur..",
            "Enviar o log ao autor..", "إرسال السجل إلى المؤلف..", "लॉग लेखक को भेजें..",
            "লগ লেখককে পাঠান..", "لاگ مصنف کو بھیجیں..", "将日志发送给作者..")
        Add("Отправить журнал автору",
            "Send the log to the author", "Надіслати журнал авторові", "Protokoll an den Autor senden",
            "Invia il registro all'autore", "Enviar el registro al autor", "Envoyer le journal à l'auteur",
            "Enviar o log ao autor", "إرسال السجل إلى المؤلف", "लॉग लेखक को भेजें",
            "লগ লেখককে পাঠান", "لاگ مصنف کو بھیجیں", "将日志发送给作者")
        Add("Не удалось собрать архив журналов. Подробности записаны в журнал программы.",
            "Could not build the log archive. The details are in the program's log.",
            "Не вдалося зібрати архів журналів. Подробиці записано в журнал програми.",
            "Das Protokollarchiv konnte nicht erstellt werden. Die Details stehen im Programmprotokoll.",
            "Impossibile creare l'archivio dei registri. I dettagli sono nel registro del programma.",
            "No se pudo crear el archivo de registros. Los detalles están en el registro del programa.",
            "Impossible de créer l'archive des journaux. Les détails sont dans le journal du programme.",
            "Não foi possível criar o arquivo de logs. Os detalhes estão no log do programa.",
            "تعذر إنشاء أرشيف السجلات. التفاصيل مسجلة في سجل البرنامج.",
            "लॉग संग्रह नहीं बन सका। विवरण प्रोग्राम के लॉग में दर्ज है।",
            "লগ আর্কাইভ তৈরি করা যায়নি। বিস্তারিত প্রোগ্রামের লগে লেখা আছে।",
            "لاگ آرکائیو نہیں بن سکا۔ تفصیلات پروگرام کے لاگ میں درج ہیں۔",
            "无法创建日志压缩包。详细信息已写入程序日志。")
        Add("Не удалось настроить брандмауэр. Попробуйте ещё раз; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..».",
            "Could not configure the firewall. Try again; if it still fails, send the log to the author: tray icon menu - «Send the log to the author..».",
            "Не вдалося налаштувати брандмауер. Спробуйте ще раз; якщо не вийде - надішліть журнал авторові: меню значка в треї - «Надіслати журнал авторові..».",
            "Die Firewall konnte nicht eingerichtet werden. Versuchen Sie es erneut; klappt es nicht, senden Sie das Protokoll an den Autor: Menü des Tray-Symbols - «Protokoll an den Autor senden..».",
            "Impossibile configurare il firewall. Riprova; se non funziona, invia il registro all'autore: menu dell'icona nell'area di notifica - «Invia il registro all'autore..».",
            "No se pudo configurar el firewall. Vuelva a intentarlo; si sigue fallando, envíe el registro al autor: menú del icono de la bandeja - «Enviar el registro al autor..».",
            "Impossible de configurer le pare-feu. Réessayez ; en cas d'échec, envoyez le journal à l'auteur : menu de l'icône de la zone de notification - «Envoyer le journal à l'auteur..».",
            "Não foi possível configurar o firewall. Tente novamente; se continuar falhando, envie o log ao autor: menu do ícone na bandeja - «Enviar o log ao autor..».",
            "تعذر إعداد جدار الحماية. حاول مرة أخرى؛ وإن لم ينجح، أرسل السجل إلى المؤلف: قائمة أيقونة شريط المهام - «إرسال السجل إلى المؤلف..».",
            "फ़ायरवॉल सेट नहीं हो सका। फिर से प्रयास करें; फिर भी न हो तो लॉग लेखक को भेजें: ट्रे आइकन का मेनू - «लॉग लेखक को भेजें..»।",
            "ফায়ারওয়াল কনফিগার করা যায়নি। আবার চেষ্টা করুন; তবুও না হলে লগ লেখককে পাঠান: ট্রে আইকনের মেনু - «লগ লেখককে পাঠান..»।",
            "فائر وال ترتیب نہیں دیا جا سکا۔ دوبارہ کوشش کریں؛ پھر بھی نہ ہو تو لاگ مصنف کو بھیجیں: ٹرے آئیکن کا مینو - «لاگ مصنف کو بھیجیں..»۔",
            "无法配置防火墙。请重试；如果仍然失败，请将日志发送给作者：托盘图标菜单 - 「将日志发送给作者..」。")
        Add("Не удалось выполнить действие. Повторите попытку; если не выйдет - отправьте журнал автору: меню значка в трее - «Отправить журнал автору..».",
            "The action did not complete. Try again; if it still fails, send the log to the author: tray icon menu - «Send the log to the author..».",
            "Не вдалося виконати дію. Спробуйте ще раз; якщо не вийде - надішліть журнал авторові: меню значка в треї - «Надіслати журнал авторові..».",
            "Die Aktion wurde nicht ausgeführt. Versuchen Sie es erneut; klappt es nicht, senden Sie das Protokoll an den Autor: Menü des Tray-Symbols - «Protokoll an den Autor senden..».",
            "L'azione non è riuscita. Riprova; se non funziona, invia il registro all'autore: menu dell'icona nell'area di notifica - «Invia il registro all'autore..».",
            "La acción no se completó. Vuelva a intentarlo; si sigue fallando, envíe el registro al autor: menú del icono de la bandeja - «Enviar el registro al autor..».",
            "L'action n'a pas abouti. Réessayez ; en cas d'échec, envoyez le journal à l'auteur : menu de l'icône de la zone de notification - «Envoyer le journal à l'auteur..».",
            "A ação não foi concluída. Tente novamente; se continuar falhando, envie o log ao autor: menu do ícone na bandeja - «Enviar o log ao autor..».",
            "لم يكتمل الإجراء. حاول مرة أخرى؛ وإن لم ينجح، أرسل السجل إلى المؤلف: قائمة أيقونة شريط المهام - «إرسال السجل إلى المؤلف..».",
            "कार्रवाई पूरी नहीं हुई। फिर से प्रयास करें; फिर भी न हो तो लॉग लेखक को भेजें: ट्रे आइकन का मेनू - «लॉग लेखक को भेजें..»।",
            "কাজটি সম্পন্ন হয়নি। আবার চেষ্টা করুন; তবুও না হলে লগ লেখককে পাঠান: ট্রে আইকনের মেনু - «লগ লেখককে পাঠান..»।",
            "کارروائی مکمل نہیں ہوئی۔ دوبارہ کوشش کریں؛ پھر بھی نہ ہو تو لاگ مصنف کو بھیجیں: ٹرے آئیکن کا مینو - «لاگ مصنف کو بھیجیں..»۔",
            "操作未完成。请重试；如果仍然失败，请将日志发送给作者：托盘图标菜单 - 「将日志发送给作者..」。")

        ' The consent dialog's words, the same entries the viewer's table carries - the
        ' dialog shows the viewer's archive, so it says the same thing in the same words.
        Add("Готов архив {0} ({1}).", "Archive {0} is ready ({1}).", "Готовий архів {0} ({1}).", "Archiv {0} ist fertig ({1}).", "L'archivio {0} è pronto ({1}).", "El archivo {0} está listo ({1}).", "L'archive {0} est prête ({1}).", "O arquivo {0} está pronto ({1}).", "الأرشيف {0} جاهز ({1}).", "संग्रह {0} तैयार है ({1})।", "আর্কাইভ {0} প্রস্তুত ({1})।", "آرکائیو {0} تیار ہے ({1})۔", "压缩包 {0} 已就绪（{1}）。")
        Add("В журнале есть пути и имена файлов, которые вы открывали, и названия ваших каталогов. Откройте архив и посмотрите, что уходит, если это важно.", "The log holds the paths and names of the files you opened and the names of your folders. Open the archive and see what is leaving, if that matters to you.", "У журналі є шляхи та імена файлів, які ви відкривали, і назви ваших тек. Відкрийте архів і подивіться, що йде, якщо це важливо.", "Das Protokoll enthält die Pfade und Namen der von Ihnen geöffneten Dateien sowie die Namen Ihrer Ordner. Öffnen Sie das Archiv und sehen Sie nach, was hinausgeht, falls das wichtig ist.", "Il log contiene i percorsi e i nomi dei file che hai aperto e i nomi delle tue cartelle. Apri l'archivio e guarda cosa parte, se per te è importante.", "El registro contiene las rutas y los nombres de los archivos que abrió y los nombres de sus carpetas. Abra el archivo y vea qué se envía, si le importa.", "Le journal contient les chemins et les noms des fichiers que vous avez ouverts ainsi que les noms de vos dossiers. Ouvrez l'archive et regardez ce qui part, si cela compte pour vous.", "O registro contém os caminhos e os nomes dos arquivos que você abriu e os nomes das suas pastas. Abra o arquivo e veja o que está saindo, se isso importa para você.", "يحتوي السجل على مسارات وأسماء الملفات التي فتحتها وأسماء مجلداتك. افتح الأرشيف وانظر ما الذي سيُرسل إن كان ذلك يهمك.", "लॉग में उन फ़ाइलों के पथ और नाम हैं जिन्हें आपने खोला, और आपके फ़ोल्डरों के नाम भी। यदि यह मायने रखता है तो संग्रह खोलकर देखें कि क्या जा रहा है।", "লগে আপনি যেসব ফাইল খুলেছেন তাদের পথ ও নাম এবং আপনার ফোল্ডারের নাম আছে। গুরুত্বপূর্ণ মনে হলে আর্কাইভ খুলে দেখুন কী পাঠানো হচ্ছে।", "لاگ میں ان فائلوں کے راستے اور نام ہیں جو آپ نے کھولیں، اور آپ کے فولڈرز کے نام بھی۔ اگر یہ اہم ہے تو آرکائیو کھول کر دیکھیں کہ کیا جا رہا ہے۔", "日志中包含你打开过的文件路径和名称，以及你的文件夹名称。如果在意，请打开压缩包看看要发送的内容。")
        Add("Открыть архив", "Open the archive", "Відкрити архів", "Archiv öffnen", "Apri l'archivio", "Abrir el archivo", "Ouvrir l'archive", "Abrir o arquivo", "فتح الأرشيف", "संग्रह खोलें", "আর্কাইভ খুলুন", "آرکائیو کھولیں", "打开压缩包")
        Add("Отправить", "Send", "Надіслати", "Senden", "Invia", "Enviar", "Envoyer", "Enviar", "إرسال", "भेजें", "পাঠান", "بھیجیں", "发送")
        Add("Архив получился больше {0} МБ - такое письмо почта не пропустит. Откройте архив и пришлите только нужное.", "The archive is larger than {0} MB - mail servers will not pass such a letter. Open it and send only what is needed.", "Архів вийшов більший за {0} МБ - такий лист пошта не пропустить. Відкрийте архів і надішліть лише потрібне.", "Das Archiv ist größer als {0} MB - eine solche Mail lässt der Server nicht durch. Öffnen Sie es und senden Sie nur das Nötige.", "L'archivio supera {0} MB - una mail del genere non passa. Aprilo e invia solo ciò che serve.", "El archivo supera los {0} MB: el correo no dejará pasar un mensaje así. Ábralo y envíe solo lo necesario.", "L'archive dépasse {0} Mo - un tel courriel ne passera pas. Ouvrez-la et n'envoyez que le nécessaire.", "O arquivo passou de {0} MB - um e-mail assim não passa. Abra-o e envie apenas o necessário.", "حجم الأرشيف أكبر من {0} ميغابايت - لن يمرّ بريد بهذا الحجم. افتحه وأرسل ما يلزم فقط.", "संग्रह {0} MB से बड़ा है - ऐसा पत्र मेल सर्वर पास नहीं करेगा। इसे खोलें और केवल आवश्यक भेजें।", "আর্কাইভ {0} MB-এর চেয়ে বড় - এমন চিঠি মেল সার্ভার পাঠাবে না। এটি খুলে শুধু প্রয়োজনীয় অংশ পাঠান।", "آرکائیو {0} MB سے بڑا ہے - ایسا خط میل سرور نہیں بھیجے گا۔ اسے کھولیں اور صرف ضروری چیز بھیجیں۔", "压缩包超过 {0} MB，这样的邮件无法发送。请打开它，只发送需要的部分。")
        Add("Почтовый клиент с вложением открыть не удалось. Архив скопирован в буфер обмена и показан в папке - вложите его в письмо вручную.", "A mail client with the attachment could not be opened. The archive is on the clipboard and shown in its folder - attach it to the letter by hand.", "Не вдалося відкрити поштовий клієнт із вкладенням. Архів скопійовано в буфер обміну та показано в теці - долучіть його до листа вручну.", "Ein Mailprogramm mit dem Anhang konnte nicht geöffnet werden. Das Archiv liegt in der Zwischenablage und wird im Ordner angezeigt - hängen Sie es von Hand an.", "Non è stato possibile aprire un client di posta con l'allegato. L'archivio è negli appunti ed è mostrato nella cartella - allegalo a mano.", "No se pudo abrir un cliente de correo con el adjunto. El archivo está en el portapapeles y se muestra en su carpeta: adjúntelo a mano.", "Impossible d'ouvrir un client de messagerie avec la pièce jointe. L'archive est dans le presse-papiers et affichée dans son dossier - joignez-la à la main.", "Não foi possível abrir um cliente de e-mail com o anexo. O arquivo está na área de transferência e é mostrado na pasta - anexe-o manualmente.", "تعذّر فتح برنامج بريد مع المرفق. تم نسخ الأرشيف إلى الحافظة وعرضه في مجلده - أرفقه بالرسالة يدويًا.", "अनुलग्नक के साथ मेल क्लाइंट नहीं खुल सका। संग्रह क्लिपबोर्ड में है और उसके फ़ोल्डर में दिखाया गया है - उसे पत्र में हाथ से संलग्न करें।", "সংযুক্তিসহ মেল ক্লায়েন্ট খোলা যায়নি। আর্কাইভ ক্লিপবোর্ডে নেওয়া হয়েছে ও ফোল্ডারে দেখানো হয়েছে - চিঠিতে নিজে যুক্ত করুন।", "منسلکہ کے ساتھ میل کلائنٹ نہیں کھل سکا۔ آرکائیو کلپ بورڈ میں ہے اور فولڈر میں دکھایا گیا ہے - اسے خط میں خود منسلک کریں۔", "无法打开带附件的邮件客户端。压缩包已复制到剪贴板并在文件夹中显示，请手动添加为附件。")
        Add("Что произошло:", "What happened:", "Що сталося:", "Was ist passiert:", "Cosa è successo:", "Qué ocurrió:", "Ce qui s'est passé :", "O que aconteceu:", "ما الذي حدث:", "क्या हुआ:", "কী ঘটেছে:", "کیا ہوا:", "发生了什么：")
        Add("Что ожидалось:", "What was expected:", "Що очікувалося:", "Was erwartet wurde:", "Cosa ci si aspettava:", "Qué se esperaba:", "Ce qui était attendu :", "O que era esperado:", "ما كان متوقعًا:", "क्या अपेक्षित था:", "কী প্রত্যাশিত ছিল:", "کیا متوقع تھا:", "期望的结果：")
        Add("Шаги для повторения:", "Steps to reproduce:", "Кроки для відтворення:", "Schritte zum Reproduzieren:", "Passi per riprodurre:", "Pasos para reproducir:", "Étapes pour reproduire :", "Passos para reproduzir:", "خطوات إعادة الإنتاج:", "दोहराने के चरण:", "পুনরাবৃত্তির ধাপ:", "دہرانے کے مراحل:", "复现步骤：")

        ' --- Ф-7: names for glyph-only controls --------------------------------------
        Add("{0} - копировать в буфер",
            "{0} - copy to clipboard", "{0} - копіювати в буфер", "{0} - in die Zwischenablage kopieren",
            "{0} - copia negli appunti", "{0} - copiar al portapapeles", "{0} - copier dans le presse-papiers",
            "{0} - copiar para a área de transferência", "{0} - نسخ إلى الحافظة", "{0} - क्लिपबोर्ड पर कॉपी करें",
            "{0} - ক্লিপবোর্ডে কপি করুন", "{0} - کلپ بورڈ پر کاپی کریں", "{0} - 复制到剪贴板")
        Add("QR-код для телефона",
            "QR code for the phone", "QR-код для телефону", "QR-Code für das Telefon", "Codice QR per il telefono",
            "Código QR para el teléfono", "Code QR pour le téléphone", "Código QR para o telefone",
            "رمز QR للهاتف", "फ़ोन के लिए QR कोड", "ফোনের জন্য QR কোড", "فون کے لیے QR کوڈ", "手机用二维码")

        ' The link under every in-app elevation to the install-trust page (ticket 037 Ф-4,
        ' INSTALL-TRUST rule 7). Worded as the page's own heading in each language, so the
        ' reader lands on the section the link promised.
        Add("Зачем нужны права администратора и как их забрать обратно",
            "What administrator rights are used for, and how to take them back",
            "Навіщо потрібні права адміністратора і як забрати їх назад",
            "Wofür Administratorrechte verwendet werden und wie Sie sie zurücknehmen",
            "A cosa servono i diritti di amministratore e come riprenderseli",
            "Para qué se usan los permisos de administrador y cómo retirarlos",
            "À quoi servent les droits administrateur et comment les reprendre",
            "Para que servem os direitos de administrador e como os retirar",
            "فيمَ تُستخدم صلاحيات المسؤول وكيف تستردّها",
            "प्रशासक अधिकार किसलिए हैं और उन्हें वापस कैसे लें",
            "প্রশাসক অধিকার কী কাজে লাগে এবং কীভাবে ফিরিয়ে নেবেন",
            "ایڈمنسٹریٹر حقوق کس کام آتے ہیں اور انہیں واپس کیسے لیں",
            "管理员权限用来做什么，以及如何收回")

        ' --- Ф-6: the consent names everything it enables, and it can be taken back ----
        Add("Чтобы делиться папками с телефоном Android, нужен небольшой фоновый SFTP-сервер и разрешение в брандмауэре Windows - для этого один раз понадобятся права администратора. Пока раздача включена, выбранные вами папки доступны по сети для чтения, а папки с разрешённой записью - и для записи. Если не отмечено «Только локальная сеть», программа также просит роутер открыть порт (UPnP/NAT-PMP) и проверяет через внешний сервис check-host.net, виден ли он из интернета. Всё это можно выключить в любой момент: Настройки менеджера - Сеть - «Выключить функции сервера..». Продолжить?",
            "Sharing folders with an Android phone needs a small background SFTP server and a Windows Firewall permission - that takes administrator rights once. While sharing is on, the folders you chose can be read over the network, and the ones opened for writing can be written too. Unless «LAN only» is ticked, the program also asks the router to open a port (UPnP/NAT-PMP) and checks through the external service check-host.net whether that port is visible from the internet. You can switch all of this off at any time: Manager settings - Network - «Switch off server features..». Continue?",
            "Щоб ділитися теками з телефоном Android, потрібен невеликий фоновий SFTP-сервер і дозвіл у брандмауері Windows - для цього один раз знадобляться права адміністратора. Поки роздача ввімкнена, вибрані вами теки доступні мережею для читання, а теки з дозволеним записом - і для запису. Якщо не позначено «Лише локальна мережа», програма також просить роутер відкрити порт (UPnP/NAT-PMP) і перевіряє через зовнішній сервіс check-host.net, чи видно його з інтернету. Усе це можна вимкнути будь-коли: Налаштування менеджера - Мережа - «Вимкнути функції сервера..». Продовжити?",
            "Zum Freigeben von Ordnern für ein Android-Telefon werden ein kleiner SFTP-Server im Hintergrund und eine Ausnahme in der Windows-Firewall benötigt - dafür sind einmalig Administratorrechte nötig. Solange die Freigabe läuft, sind die gewählten Ordner im Netzwerk lesbar, und Ordner mit erlaubtem Schreibzugriff auch beschreibbar. Ist «Nur lokales Netzwerk» nicht markiert, bittet das Programm außerdem den Router, einen Port zu öffnen (UPnP/NAT-PMP), und prüft über den externen Dienst check-host.net, ob dieser aus dem Internet sichtbar ist. Das alles lässt sich jederzeit abschalten: Manager-Einstellungen - Netzwerk - «Serverfunktionen ausschalten..». Fortfahren?",
            "Per condividere cartelle con un telefono Android servono un piccolo server SFTP in background e un'autorizzazione in Windows Firewall: per questo servono una volta i diritti di amministratore. Finché la condivisione è attiva, le cartelle scelte sono leggibili in rete e quelle con scrittura consentita anche scrivibili. Se «Solo rete locale» non è selezionato, il programma chiede anche al router di aprire una porta (UPnP/NAT-PMP) e verifica tramite il servizio esterno check-host.net se è visibile da Internet. Puoi disattivare tutto in qualsiasi momento: Impostazioni del gestore - Rete - «Disattiva le funzioni server..». Continuare?",
            "Para compartir carpetas con un teléfono Android se necesita un pequeño servidor SFTP en segundo plano y un permiso en el Firewall de Windows; para ello hacen falta una vez derechos de administrador. Mientras se comparte, las carpetas elegidas se pueden leer por la red y las que tienen la escritura permitida también se pueden escribir. Si «Solo red local» no está marcado, el programa además pide al router que abra un puerto (UPnP/NAT-PMP) y comprueba con el servicio externo check-host.net si ese puerto es visible desde internet. Todo esto se puede desactivar en cualquier momento: Ajustes del gestor - Red - «Desactivar las funciones de servidor..». ¿Continuar?",
            "Pour partager des dossiers avec un téléphone Android, il faut un petit serveur SFTP en arrière-plan et une autorisation dans le Pare-feu Windows - ce qui demande une fois les droits d'administrateur. Tant que le partage est actif, les dossiers choisis sont lisibles sur le réseau, et ceux ouverts en écriture sont aussi modifiables. Si «Réseau local uniquement» n'est pas coché, le programme demande aussi au routeur d'ouvrir un port (UPnP/NAT-PMP) et vérifie via le service externe check-host.net s'il est visible depuis Internet. Tout cela peut être désactivé à tout moment : Paramètres du gestionnaire - Réseau - «Désactiver les fonctions serveur..». Continuer ?",
            "Para compartilhar pastas com um telefone Android, são necessários um pequeno servidor SFTP em segundo plano e uma permissão no Firewall do Windows - para isso, direitos de administrador uma vez. Enquanto o compartilhamento está ativo, as pastas escolhidas podem ser lidas pela rede, e as com gravação permitida também podem ser gravadas. Se «Apenas rede local» não estiver marcado, o programa também pede ao roteador para abrir uma porta (UPnP/NAT-PMP) e verifica pelo serviço externo check-host.net se ela é visível da internet. Tudo isso pode ser desligado a qualquer momento: Definições do gestor - Rede - «Desligar as funções de servidor..». Continuar?",
            "لمشاركة المجلدات مع هاتف Android يلزم خادم SFTP صغير يعمل في الخلفية وإذن في جدار حماية Windows - ويتطلب ذلك صلاحيات المسؤول مرة واحدة. ما دامت المشاركة قيد التشغيل، تكون المجلدات التي اخترتها قابلة للقراءة عبر الشبكة، والمجلدات المسموح فيها بالكتابة قابلة للكتابة أيضًا. إذا لم يُحدَّد «الشبكة المحلية فقط»، يطلب البرنامج أيضًا من الموجّه فتح منفذ (UPnP/NAT-PMP) ويتحقق عبر الخدمة الخارجية check-host.net مما إذا كان المنفذ مرئيًا من الإنترنت. يمكنك إيقاف كل ذلك في أي وقت: إعدادات المدير - الشبكة - «إيقاف وظائف الخادم..». هل تريد المتابعة؟",
            "Android फ़ोन के साथ फ़ोल्डर साझा करने के लिए एक छोटा पृष्ठभूमि SFTP सर्वर और Windows फ़ायरवॉल में अनुमति चाहिए - इसके लिए एक बार व्यवस्थापक अधिकार लगेंगे। जब तक साझाकरण चालू है, आपके चुने फ़ोल्डर नेटवर्क पर पढ़े जा सकते हैं, और जिनमें लिखने की अनुमति है उनमें लिखा भी जा सकता है। यदि «केवल स्थानीय नेटवर्क» चिह्नित नहीं है, तो प्रोग्राम राउटर से पोर्ट खोलने (UPnP/NAT-PMP) का अनुरोध भी करता है और बाहरी सेवा check-host.net से जाँचता है कि वह इंटरनेट से दिखाई देता है या नहीं। यह सब कभी भी बंद किया जा सकता है: मैनेजर सेटिंग्स - नेटवर्क - «सर्वर सुविधाएँ बंद करें..»। जारी रखें?",
            "Android ফোনের সাথে ফোল্ডার শেয়ার করতে একটি ছোট পটভূমি SFTP সার্ভার ও Windows ফায়ারওয়ালে অনুমতি দরকার - এর জন্য একবার প্রশাসকের অধিকার লাগবে। শেয়ার চালু থাকা অবস্থায় আপনার বেছে নেওয়া ফোল্ডারগুলি নেটওয়ার্কে পড়া যায়, আর যেগুলিতে লেখার অনুমতি আছে সেগুলিতে লেখাও যায়। «শুধু স্থানীয় নেটওয়ার্ক» চিহ্নিত না থাকলে প্রোগ্রাম রাউটারকে একটি পোর্ট খুলতেও বলে (UPnP/NAT-PMP) এবং বাহ্যিক পরিষেবা check-host.net দিয়ে যাচাই করে সেটি ইন্টারনেট থেকে দেখা যায় কি না। এসব যেকোনো সময় বন্ধ করা যায়: ম্যানেজার সেটিংস - নেটওয়ার্ক - «সার্ভার বৈশিষ্ট্য বন্ধ করুন..»। চালিয়ে যাবেন?",
            "Android فون کے ساتھ فولڈر شیئر کرنے کے لیے ایک چھوٹا پس منظر SFTP سرور اور Windows فائر وال میں اجازت درکار ہے - اس کے لیے ایک بار ایڈمنسٹریٹر کے حقوق لگیں گے۔ جب تک شیئرنگ چل رہی ہے، آپ کے منتخب فولڈر نیٹ ورک پر پڑھے جا سکتے ہیں، اور جن میں لکھنے کی اجازت ہے ان میں لکھا بھی جا سکتا ہے۔ اگر «صرف مقامی نیٹ ورک» منتخب نہیں ہے تو پروگرام راؤٹر سے پورٹ کھولنے (UPnP/NAT-PMP) کی درخواست بھی کرتا ہے اور بیرونی سروس check-host.net سے جانچتا ہے کہ آیا وہ انٹرنیٹ سے نظر آتا ہے۔ یہ سب کسی بھی وقت بند کیا جا سکتا ہے: مینیجر کی ترتیبات - نیٹ ورک - «سرور کی خصوصیات بند کریں..»۔ جاری رکھیں؟",
            "要与 Android 手机共享文件夹，需要一个小型后台 SFTP 服务器以及 Windows 防火墙中的一条许可规则，这需要一次管理员权限。共享开启期间，你选择的文件夹可以通过网络读取，允许写入的文件夹也可以写入。如果未勾选「仅局域网」，程序还会请求路由器开放一个端口（UPnP/NAT-PMP），并通过外部服务 check-host.net 检查该端口是否能从互联网访问。所有这些都可以随时关闭：管理器设置 - 网络 - 「关闭服务器功能..」。继续吗？")
        Add("Выключить функции сервера..",
            "Switch off server features..", "Вимкнути функції сервера..", "Serverfunktionen ausschalten..",
            "Disattiva le funzioni server..", "Desactivar las funciones de servidor..", "Désactiver les fonctions serveur..",
            "Desligar as funções de servidor..", "إيقاف وظائف الخادم..", "सर्वर सुविधाएँ बंद करें..",
            "সার্ভার বৈশিষ্ট্য বন্ধ করুন..", "سرور کی خصوصیات بند کریں..", "关闭服务器功能..")
        Add("Выключить функции сервера? Раздача остановится, подключённые телефоны будут отключены, а разрешение в брандмауэре Windows будет удалено - для этого понадобятся права администратора. Список папок и пароль сохранятся; включить всё снова можно в любой момент.",
            "Switch off server features? Sharing stops, connected phones are disconnected, and the Windows Firewall permission is removed - that needs administrator rights. The folder list and the password are kept; you can switch everything back on at any time.",
            "Вимкнути функції сервера? Роздача зупиниться, підключені телефони буде від'єднано, а дозвіл у брандмауері Windows буде видалено - для цього знадобляться права адміністратора. Список тек і пароль збережуться; увімкнути все знову можна будь-коли.",
            "Serverfunktionen ausschalten? Die Freigabe endet, verbundene Telefone werden getrennt, und die Ausnahme in der Windows-Firewall wird entfernt - dafür sind Administratorrechte nötig. Ordnerliste und Kennwort bleiben erhalten; alles lässt sich jederzeit wieder einschalten.",
            "Disattivare le funzioni server? La condivisione si ferma, i telefoni connessi vengono disconnessi e l'autorizzazione in Windows Firewall viene rimossa: servono i diritti di amministratore. L'elenco delle cartelle e la password restano; puoi riattivare tutto in qualsiasi momento.",
            "¿Desactivar las funciones de servidor? El uso compartido se detiene, los teléfonos conectados se desconectan y se elimina el permiso del Firewall de Windows; para ello hacen falta derechos de administrador. La lista de carpetas y la contraseña se conservan; puede volver a activarlo todo en cualquier momento.",
            "Désactiver les fonctions serveur ? Le partage s'arrête, les téléphones connectés sont déconnectés et l'autorisation du Pare-feu Windows est supprimée - cela demande les droits d'administrateur. La liste des dossiers et le mot de passe sont conservés ; vous pouvez tout réactiver à tout moment.",
            "Desligar as funções de servidor? O compartilhamento para, os telefones conectados são desconectados e a permissão no Firewall do Windows é removida - para isso são necessários direitos de administrador. A lista de pastas e a senha são mantidas; é possível religar tudo a qualquer momento.",
            "هل تريد إيقاف وظائف الخادم؟ ستتوقف المشاركة، وسيُقطع اتصال الهواتف المتصلة، وسيُحذف الإذن من جدار حماية Windows - ويتطلب ذلك صلاحيات المسؤول. تبقى قائمة المجلدات وكلمة المرور؛ ويمكنك إعادة تشغيل كل شيء في أي وقت.",
            "सर्वर सुविधाएँ बंद करें? साझाकरण रुक जाएगा, जुड़े फ़ोन डिस्कनेक्ट हो जाएँगे, और Windows फ़ायरवॉल की अनुमति हटा दी जाएगी - इसके लिए व्यवस्थापक अधिकार चाहिए। फ़ोल्डर सूची और पासवर्ड बने रहेंगे; सब कुछ कभी भी फिर से चालू किया जा सकता है।",
            "সার্ভার বৈশিষ্ট্য বন্ধ করবেন? শেয়ার বন্ধ হবে, সংযুক্ত ফোনগুলি বিচ্ছিন্ন হবে, এবং Windows ফায়ারওয়ালের অনুমতি মুছে ফেলা হবে - এর জন্য প্রশাসকের অধিকার লাগবে। ফোল্ডারের তালিকা ও পাসওয়ার্ড থেকে যাবে; সবকিছু যেকোনো সময় আবার চালু করা যায়।",
            "سرور کی خصوصیات بند کریں؟ شیئرنگ رک جائے گی، جڑے ہوئے فون منقطع ہو جائیں گے، اور Windows فائر وال کی اجازت ہٹا دی جائے گی - اس کے لیے ایڈمنسٹریٹر کے حقوق درکار ہیں۔ فولڈرز کی فہرست اور پاس ورڈ برقرار رہیں گے؛ سب کچھ کسی بھی وقت دوبارہ آن کیا جا سکتا ہے۔",
            "要关闭服务器功能吗？共享将停止，已连接的手机将被断开，Windows 防火墙中的许可将被删除，这需要管理员权限。文件夹列表和密码会保留，可以随时重新开启全部功能。")
        Add("Ничего не изменено: права администратора не получены. Раздача остановлена - её можно снова включить в главном окне.",
            "Nothing was changed: administrator rights were not granted. Sharing is stopped - you can switch it on again in the main window.",
            "Нічого не змінено: права адміністратора не отримано. Роздачу зупинено - її можна знову ввімкнути в головному вікні.",
            "Nichts wurde geändert: Es wurden keine Administratorrechte erteilt. Die Freigabe ist beendet - sie lässt sich im Hauptfenster wieder einschalten.",
            "Nulla è stato modificato: i diritti di amministratore non sono stati concessi. La condivisione è ferma: puoi riattivarla nella finestra principale.",
            "No se ha cambiado nada: no se concedieron derechos de administrador. El uso compartido está detenido; puede volver a activarlo en la ventana principal.",
            "Rien n'a été modifié : les droits d'administrateur n'ont pas été accordés. Le partage est arrêté - vous pouvez le réactiver dans la fenêtre principale.",
            "Nada foi alterado: os direitos de administrador não foram concedidos. O compartilhamento está parado - é possível religá-lo na janela principal.",
            "لم يتغير شيء: لم تُمنح صلاحيات المسؤول. المشاركة متوقفة - يمكنك تشغيلها من جديد في النافذة الرئيسية.",
            "कुछ नहीं बदला: व्यवस्थापक अधिकार नहीं मिले। साझाकरण रुका हुआ है - इसे मुख्य विंडो में फिर से चालू किया जा सकता है।",
            "কিছুই বদলায়নি: প্রশাসকের অধিকার পাওয়া যায়নি। শেয়ার বন্ধ আছে - প্রধান উইন্ডোতে আবার চালু করা যায়।",
            "کچھ تبدیل نہیں ہوا: ایڈمنسٹریٹر کے حقوق نہیں ملے۔ شیئرنگ رکی ہوئی ہے - اسے مرکزی ونڈو میں دوبارہ آن کیا جا سکتا ہے۔",
            "未做任何更改：未获得管理员权限。共享已停止，可以在主窗口中重新开启。")
        Add("Разрешение в брандмауэре удалено, но функции сервера включены установщиком для всего компьютера. Чтобы выключить их полностью, измените установку: Параметры Windows - Приложения.",
            "The firewall permission is removed, but the installer switched server features on for the whole computer. To switch them off completely, modify the installation: Windows Settings - Apps.",
            "Дозвіл у брандмауері видалено, але функції сервера ввімкнув інсталятор для всього комп'ютера. Щоб вимкнути їх повністю, змініть встановлення: Параметри Windows - Програми.",
            "Die Firewall-Ausnahme ist entfernt, aber das Installationsprogramm hat die Serverfunktionen für den ganzen Computer eingeschaltet. Um sie ganz auszuschalten, ändern Sie die Installation: Windows-Einstellungen - Apps.",
            "L'autorizzazione del firewall è stata rimossa, ma le funzioni server sono state attivate dal programma di installazione per l'intero computer. Per disattivarle del tutto, modifica l'installazione: Impostazioni di Windows - App.",
            "Se eliminó el permiso del firewall, pero el instalador activó las funciones de servidor para todo el equipo. Para desactivarlas por completo, modifique la instalación: Configuración de Windows - Aplicaciones.",
            "L'autorisation du pare-feu est supprimée, mais le programme d'installation a activé les fonctions serveur pour tout l'ordinateur. Pour les désactiver complètement, modifiez l'installation : Paramètres Windows - Applications.",
            "A permissão do firewall foi removida, mas o instalador ligou as funções de servidor para todo o computador. Para desligá-las por completo, altere a instalação: Configurações do Windows - Aplicativos.",
            "أُزيل إذن جدار الحماية، لكن برنامج التثبيت فعّل وظائف الخادم للكمبيوتر بأكمله. لإيقافها تمامًا، عدّل التثبيت: إعدادات Windows - التطبيقات.",
            "फ़ायरवॉल अनुमति हटा दी गई है, लेकिन इंस्टॉलर ने सर्वर सुविधाएँ पूरे कंप्यूटर के लिए चालू की हैं। उन्हें पूरी तरह बंद करने के लिए इंस्टॉलेशन बदलें: Windows सेटिंग्स - ऐप्स।",
            "ফায়ারওয়ালের অনুমতি মুছে ফেলা হয়েছে, কিন্তু ইনস্টলার পুরো কম্পিউটারের জন্য সার্ভার বৈশিষ্ট্য চালু করেছে। সম্পূর্ণ বন্ধ করতে ইনস্টলেশন পরিবর্তন করুন: Windows সেটিংস - অ্যাপস।",
            "فائر وال کی اجازت ہٹا دی گئی ہے، لیکن انسٹالر نے سرور کی خصوصیات پورے کمپیوٹر کے لیے آن کی ہیں۔ انہیں مکمل طور پر بند کرنے کے لیے انسٹالیشن تبدیل کریں: Windows کی ترتیبات - ایپس۔",
            "防火墙许可已删除，但服务器功能是安装程序为整台电脑开启的。要完全关闭，请修改安装：Windows 设置 - 应用。")

    End Sub

End Class
