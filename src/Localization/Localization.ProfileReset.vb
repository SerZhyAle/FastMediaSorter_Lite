Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>
''' Specification 169 §7: Localization strings for profile reset.
''' Order of arguments after Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
''' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddProfileResetStrings()

        Add("Сброс настроек",
            "Reset settings",
            "Скидання налаштувань",
            "Einstellungen zurücksetzen",
            "Reimposta impostazioni",
            "Restablecer ajustes",
            "Réinitialiser les paramètres",
            "Redefinir configurações",
            "إعادة ضبط الإعدادات",
            "सेटिंग्स रीसेट करें",
            "সেটিংস রিসেট করুন",
            "ترتیبات دوبارہ ترتیب دیں",
            "重置设置")

        Add("Сбросить настройки",
            "Reset settings",
            "Скинути налаштування",
            "Einstellungen zurücksetzen",
            "Reimposta impostazioni",
            "Restablecer ajustes",
            "Réinitialiser les paramètres",
            "Redefinir configurações",
            "إعادة ضبط الإعدادات",
            "सेटिंग्स रीसेट करें",
            "সেটিংস রিসেট করুন",
            "ترتیبات دوبارہ ترتیب دیں",
            "重置设置")

        Add("Вернёт настройки просмотрщика к значениям по умолчанию. Язык, тема, пароли и история останутся. Прежние значения сохранятся в файл.",
            "Resets the viewer settings to defaults. Language, theme, passwords, and history are kept. Previous values are saved to a file.",
            "Поверне налаштування переглядача до значень за замовчуванням. Мова, тема, паролі та історія залишаться. Попередні значення збережуться у файл.",
            "Setzt die Viewer-Einstellungen auf die Standardwerte zurück. Sprache, Design, Passwörter und Verlauf bleiben erhalten. Bisherige Werte werden in einer Datei gespeichert.",
            "Ripristina le impostazioni del visualizzatore ai valori predefiniti. Lingua, tema, password e cronologia rimangono. I valori precedenti verranno salvati in un file.",
            "Restablece los ajustes del visor a los valores predeterminados. El idioma, el tema, las contraseñas y el historial se conservan. Los valores anteriores se guardan en un archivo.",
            "Rétablit les paramètres de la visionneuse par défaut. La langue, le thème, les mots de passe et l'historique sont conservés. Les valeurs précédentes sont enregistrées dans un fichier.",
            "Restaura as configurações do visualizador para os padrões. Idioma, tema, senhas e histórico são mantidos. Os valores anteriores são salvos em um arquivo.",
            "يعيد إعدادات العارض إلى القيم الافتراضية. تبقى اللغة والمظهر وكلمات المرور والسجل. تُحفظ القيم السابقة في ملف.",
            "व्यूअर सेटिंग्स को डिफ़ॉल्ट पर रीसेट करेगा। भाषा, थीम, पासवर्ड और इतिहास बने रहेंगे। पिछले मान फ़ाइल में सहेजे जाएँगे।",
            "ভিউয়ার সেটিংস ডিফল্টে ফিরিয়ে আনবে। ভাষা, থিম, পাসওয়ার্ড এবং ইতিহাস থাকবে। পূর্ববর্তী মান একটি ফাইলে সংরক্ষিত হবে।",
            "ویور ترتیبات کو پہلے سے طے شدہ پر بحال کرے گا۔ زبان، تھیم، پاس ورڈ اور تاریخ محفوظ رہیں گے۔ پچھلی اقدار فائل میں محفوظ ہو جائیں گی۔",
            "将查看器设置恢复为默认值。语言、主题、密码和历史记录将保留。先前的值将保存到文件中。")

        Add("Сбрасываются получатели, вид и поведение, видео и звук, OCR и перевод, положение окон. Не затрагиваются: язык и тема (они общие с Share Manager), сохранённые пароли и ключи, список ресурсов, история папок и файлов. Прежние значения записываются в файл, который можно открыть двойным щелчком, закрыв программу. Сброс выполняется при следующем запуске.",
            "Resets recipients, view and behaviour, video and sound, OCR and translation, window layout. Kept: language and theme (shared with Share Manager), saved passwords and keys, resource list, folder and file history. Previous values are saved to a file that can be restored with a double-click after closing the app. The reset takes effect on the next launch.",
            "Скидаються отримувачі, вигляд і поведінка, відео та звук, OCR і переклад, положення вікон. Не зачіпаються: мова та тема (спільні з Share Manager), збережені паролі та ключі, список ресурсів, історія папок і файлів. Попередні значення записуються у файл, який можна відкрити подвійним клацанням, закривши програму. Скидання виконується під час наступного запуску.",
            "Zurückgesetzt werden Empfänger, Ansicht und Verhalten, Video und Ton, OCR und Übersetzung, Fensterpositionen. Beibehalten werden: Sprache und Design (mit Share Manager geteilt), gespeicherte Passwörter und Schlüssel, Ressourcenliste, Ordner- und Dateiverlauf. Bisherige Werte werden in einer Datei gespeichert, die nach dem Schließen des Programms per Doppelklick geöffnet werden kann. Das Zurücksetzen erfolgt beim nächsten Start.",
            "Vengono reimpostati destinatari, aspetto e comportamento, video e audio, OCR e traduzione, posizione delle finestre. Rimangono invariati: lingua e tema (condivisi con Share Manager), password e chiavi salvate, elenco risorse, cronologia di cartelle e file. I valori precedenti vengono salvati in un file apribile con un doppio clic dopo aver chiuso il programma. Il ripristino viene eseguito al prossimo avvio.",
            "Se restablecen destinatarios, aspecto y comportamiento, vídeo y sonido, OCR y traducción, posición de ventanas. No se ven afectados: idioma y tema (compartidos con Share Manager), contraseñas y claves guardadas, lista de recursos, historial de carpetas y archivos. Los valores anteriores se guardan en un archivo que se puede abrir con doble clic tras cerrar la aplicación. El restablecimiento se aplica en el siguiente inicio.",
            "Sont réinitialisés les destinataires, l'affichage et le comportement, la vidéo et le son, l'OCR et la traduction, la disposition des fenêtres. Restent inchangés : langue et thème (partagés avec Share Manager), mots de passe et clés enregistrés, liste des ressources, historique des dossiers et fichiers. Les valeurs précédentes sont enregistrées dans un fichier qui peut être ouvert d'un double-clic après avoir fermé l'application. La réinitialisation s'applique au prochain démarrage.",
            "São redefinidos destinatários, aparência e comportamento, vídeo e som, OCR e tradução, posição das janelas. Não são afetados: idioma e tema (compartilhados com o Share Manager), senhas e chaves salvas, lista de recursos, histórico de pastas e arquivos. Os valores anteriores são gravados em um arquivo que pode ser aberto com clique duplo após fechar o programa. A redefinição entra em vigor na próxima inicialização.",
            "تتم إعادة ضبط الوجهات، والمظهر والسلوك، والفيديو والصوت، والتعرف الضوئي والترجمة، ومواضع النوافذ. لا تتأثر: اللغة والمظهر (مشتركان مع Share Manager)، وكلمات المرور والمفاتيح المحفوظة، وقائمة الموارد، وسجل المجلدات والملفات. تُسجل القيم السابقة في ملف يمكن فتحه بالنقر المزدوج بعد إغلاق البرنامج. يتم تطبيق إعادة الضبط عند التشغيل التالي.",
            "प्राप्तकर्ता, दृश्य और व्यवहार, वीडियो और ध्वनि, OCR और अनुवाद, विंडो की स्थिति रीसेट हो जाते हैं। अप्रभावित: भाषा और थीम (Share Manager के साथ साझा), सहेजे गए पासवर्ड और कुंजियाँ, संसाधन सूची, फ़ोल्डर और फ़ाइल इतिहास। पिछले मान एक फ़ाइल में लिखे जाते हैं जिसे प्रोग्राम बंद करने के बाद डबल-क्लिक करके खोला जा सकता है। रीसेट अगली बार शुरू होने पर लागू होगा।",
            "প্রাপক, চেহারা ও আচরণ, ভিডিও ও শব্দ, OCR ও অনুবাদ, উইন্ডোর অবস্থান রিসেট করা হয়। অপরিবর্তিত থাকে: ভাষা ও থিম (Share Manager-এর সাথে শেয়ার করা), সংরক্ষিত পাসওয়ার্ড ও কী, রিসোর্স তালিকা, ফোল্ডার ও ফাইলের ইতিহাস। পূর্ববর্তী মান এমন একটি ফাইলে লেখা হয় যা প্রোগ্রাম বন্ধ করার পরে ডাবল-ক্লিক করে খোলা যায়। পরবর্তী শুরুতে রিসেট কার্যকর হয়।",
            "وصول کنندگان، شکل و رویہ، ویڈیو اور آواز، OCR اور ترجمہ، ونڈو کی پوزیشنیں دوبارہ ترتیب دی جاتی ہیں۔ غیر متاثر: زبان اور تھیم (Share Manager کے ساتھ مشترک)، محفوظ شدہ پاس ورڈ اور کلیدیں، وسائل کی فہرست، فولڈر اور فائل کی تاریخ۔ پچھلی اقدار ایک فائل میں لکھی جاتی ہیں جسے پروگرام بند کرنے کے بعد ڈبل کلک کر کے کھولا جا سکتا ہے۔ اگلی بار شروع ہونے پر ری سیٹ نافذ ہوتا ہے۔",
            "重置接收方、外观和行为、视频和声音、OCR与翻译、窗口位置。不受影响：语言和主题（与 Share Manager 共享）、保存的密码和密钥、资源列表、文件夹和文件历史记录。先前的值将写入一个文件，关闭程序后双击该文件即可恢复。重置将在下次启动时生效。")

        Add("Сброс настроек выполняется из первого окна программы. Закройте это окно и откройте настройки там.",
            "Settings reset is performed from the primary window. Close this window and open settings there.",
            "Скидання налаштувань виконується з першого вікна програми. Закрийте це вікно та відкрийте налаштування там.",
            "Das Zurücksetzen der Einstellungen erfolgt über das erste Programmfenster. Schließen Sie dieses Fenster und öffnen Sie die Einstellungen dort.",
            "Il ripristino delle impostazioni viene eseguito dalla finestra principale. Chiudi questa finestra e apri le impostazioni da lì.",
            "El restablecimiento de ajustes se realiza desde la primera ventana de la aplicación. Cierre esta ventana y abra los ajustes allí.",
            "La réinitialisation des paramètres s'effectue depuis la première fenêtre de l'application. Fermez cette fenêtre et ouvrez les paramètres là-bas.",
            "A redefinição de configurações é realizada na primeira janela do programa. Feche esta janela e abra as configurações lá.",
            "تتم إعادة ضبط الإعدادات من النافذة الأولى للبرنامج. أغلق هذه النافذة وافتح الإعدادات هناك.",
            "सेटिंग्स रीसेट प्रोग्राम की पहली विंडो से किया जाता है। इस विंडो को बंद करें और सेटिंग्स वहाँ खोलें।",
            "সেটিংস রিসেট প্রোগ্রামের প্রথম উইন্ডো থেকে করা হয়। এই উইন্ডোটি বন্ধ করুন এবং সেখানে সেটিংস খুলুন।",
            "ترتیبات کو دوبارہ ترتیب دینا پروگرام کی پہلی ونڈو سے کیا جاتا ہے۔ اس ونڈو کو بند کریں اور وہاں ترتیبات کھولیں۔",
            "重置设置只能在程序的主窗口中执行。请关闭此窗口并在主窗口中打开设置。")

        Add("Сбросить все настройки просмотрщика к значениям по умолчанию?",
            "Reset all viewer settings to defaults?",
            "Скинути всі налаштування переглядача до значень за замовчуванням?",
            "Alle Viewer-Einstellungen auf die Standardwerte zurücksetzen?",
            "Reimpostare tutte le impostazioni del visualizzatore sui valori predefiniti?",
            "¿Restablecer todos los ajustes del visor a los valores predeterminados?",
            "Réinitialiser tous les paramètres de la visionneuse aux valeurs par défaut ?",
            "Redefinir todas as configurações do visualizador para os padrões?",
            "هل تريد إعادة ضبط جميع إعدادات العارض إلى القيم الافتراضية؟",
            "क्या व्यूअर की सभी सेटिंग्स को डिफ़ॉल्ट पर रीसेट करना है?",
            "ভিউয়ারের সমস্ত সেটিংস ডিফল্টে রিসেট করবেন?",
            "کیا ویور کی تمام ترتیبات کو پہلے سے طے شدہ پر دوبارہ ترتیب دیں؟",
            "将所有查看器设置重置为默认值？")

        Add("Программа закроется, а настройки вернутся к умолчаниям при следующем запуске. Получатели (клавиши 0-9 и Delete), вид, видео, OCR и положение окон будут сброшены. Язык, тема, пароли, список ресурсов и история останутся. Прежние значения сохранятся в файл.",
            "The application will close and settings will return to defaults on the next launch. Recipients (keys 0-9 and Delete), view, video, OCR, and window layout will be reset. Language, theme, passwords, resource list, and history will be kept. Previous values will be saved to a file.",
            "Програма закриється, а налаштування повернуться до значень за замовчуванням при наступному запуску. Отримувачі (клавіші 0-9 та Delete), вигляд, відео, OCR і положення вікон будуть скинуті. Мова, тема, паролі, список ресурсів та історія залишаться. Попередні значення збережуться у файл.",
            "Das Programm wird geschlossen und die Einstellungen werden beim nächsten Start auf die Standardwerte zurückgesetzt. Empfänger (Tasten 0-9 und Entf), Ansicht, Video, OCR und Fensterpositionen werden zurückgesetzt. Sprache, Design, Passwörter, Ressourcenliste und Verlauf bleiben erhalten. Bisherige Werte werden in einer Datei gespeichert.",
            "Il programma si chiuderà e le impostazioni torneranno ai valori predefiniti al prossimo avvio. Destinatari (tasti 0-9 e Canc), aspetto, video, OCR e posizione delle finestre verranno reimpostati. Lingua, tema, password, elenco risorse e cronologia rimarranno. I valori precedenti verranno salvati in un file.",
            "La aplicación se cerrará y los ajustes volverán a los valores predeterminados en el siguiente inicio. Se restablecerán los destinatarios (teclas 0-9 y Supr), la vista, el vídeo, el OCR y la posición de las ventanas. El idioma, el tema, las contraseñas, la lista de recursos y el historial se conservarán. Los valores anteriores se guardarán en un archivo.",
            "L'application se fermera et les paramètres reviendront aux valeurs par défaut au prochain démarrage. Les destinataires (touches 0-9 et Suppr), l'affichage, la vidéo, l'OCR et la disposition des fenêtres seront réinitialisés. La langue, le thème, les mots de passe, la liste des ressources et l'historique seront conservés. Les valeurs précédentes seront enregistrées dans un fichier.",
            "O programa será fechado e as configurações voltarão aos padrões na próxima inicialização. Destinatários (teclas 0-9 e Delete), exibição, vídeo, OCR e posição das janelas serão redefinidos. Idioma, tema, senhas, lista de recursos e histórico serão mantidos. Os valores anteriores serão salvos em um arquivo.",
            "سيُغلق البرنامج وستعود الإعدادات إلى القيم الافتراضية عند التشغيل التالي. ستتم إعادة ضبط الوجهات (المفاتيح 0-9 و Delete) والعرض والفيديو والتعرف الضوئي ومواضع النوافذ. ستبقى اللغة والمظهر وكلمات المرور وقائمة الموارد والسجل. ستُحفظ القيم السابقة في ملف.",
            "प्रोग्राम बंद हो जाएगा और अगली बार शुरू होने पर सेटिंग्स डिफ़ॉल्ट पर वापस आ जाएँगी। प्राप्तकर्ता (कुंजियाँ 0-9 और Delete), दृश्य, वीडियो, OCR और विंडो स्थिति रीसेट हो जाएँगे। भाषा, थीम, पासवर्ड, संसाधन सूची और इतिहास बने रहेंगे। पिछले मान फ़ाइल में सहेजे जाएँगे।",
            "প্রোগ্রামটি বন্ধ হয়ে যাবে এবং পরবর্তী শুরুতে সেটিংস ডিফল্টে ফিরে আসবে। প্রাপক (0-9 এবং Delete কী), দৃশ্য, ভিডিও, OCR এবং উইন্ডোর অবস্থান রিসেট করা হবে। ভাষা, থিম, পাসওয়ার্ড, রিসোর্স তালিকা এবং ইতিহাস থাকবে। পূর্ববর্তী মান একটি ফাইলে সংরক্ষিত হবে।",
            "پروگرام بند ہو جائے گا اور اگلی بار شروع ہونے پر ترتیبات پہلے سے طے شدہ پر واپس آ جائیں گی۔ وصول کنندگان (0-9 اور Delete کیز)، منظر، ویڈیو، OCR اور ونڈو کی پوزیشنیں دوبارہ ترتیب دی جائیں گی۔ زبان، تھیم، پاس ورڈ، وسائل کی فہرست اور تاریخ محفوظ رہیں گے۔ پچھلی اقدار فائل میں محفوظ ہو جائیں گی۔",
            "程序将关闭，并在下次启动时将设置恢复为默认值。接收方（按键 0-9 和 Delete）、视图、视频、OCR 和窗口位置将被重置。语言、主题、密码、资源列表和历史记录将被保留。先前的值将保存到文件中。")

        Add("Сбросить и закрыть",
            "Reset and close",
            "Скинути та закрити",
            "Zurücksetzen und schließen",
            "Reimposta e chiudi",
            "Restablecer y cerrar",
            "Réinitialiser et fermer",
            "Redefinir e fechar",
            "إعادة ضبط وإغلاق",
            "रीसेट करें और बंद करें",
            "রিসেট করুন এবং বন্ধ করুন",
            "دوبارہ ترتیب دیں اور بند کریں",
            "重置并关闭")

        Add("Настройки уже имеют значения по умолчанию - сбрасывать нечего.",
            "Settings are already at default values - nothing to reset.",
            "Налаштування вже мають значення за замовчуванням - скидати нічого.",
            "Die Einstellungen haben bereits die Standardwerte - nichts zurückzusetzen.",
            "Le impostazioni hanno già i valori predefiniti: niente da reimpostare.",
            "Los ajustes ya tienen los valores predeterminados: nada que restablecer.",
            "Les paramètres ont déjà les valeurs par défaut : rien à réinitialiser.",
            "As configurações já estão nos valores padrão: nada a redefinir.",
            "الإعدادات بالقيم الافتراضية بالفعل - لا يوجد ما يلزم إعادة ضبطه.",
            "सेटिंग्स पहले से ही डिफ़ॉल्ट मानों पर हैं - रीसेट करने के लिए कुछ नहीं है।",
            "সেটিংস ইতিমধ্যে ডিফল্ট মানে রয়েছে - রিসেট করার মতো কিছু নেই।",
            "ترتیبات پہلے ہی طے شدہ اقدار پر ہیں - دوبارہ ترتیب دینے کے لیے کچھ نہیں ہے۔",
            "设置已是默认值 - 无需重置。")

        Add("Программа ещё не закрыта. Настройки будут сброшены при её следующем запуске.",
            "The application has not closed yet. Settings will be reset on its next launch.",
            "Програма ще не закрита. Налаштування будуть скинуті при її наступному запуску.",
            "Das Programm wurde noch nicht geschlossen. Die Einstellungen werden beim nächsten Start zurückgesetzt.",
            "Il programma non è ancora chiuso. Le impostazioni verranno reimpostate al suo prossimo avvio.",
            "La aplicación aún no se ha cerrado. Los ajustes se restablecerán en el siguiente inicio.",
            "L'application n'est pas encore fermée. Les paramètres seront réinitialisés lors de son prochain démarrage.",
            "O programa ainda não foi fechado. As configurações serão redefinidas na próxima inicialização.",
            "لم يُغلق البرنامج بعد. ستتم إعادة ضبط الإعدادات عند تشغيله التالي.",
            "प्रोग्राम अभी बंद नहीं हुआ है। इसके अगली बार शुरू होने पर सेटिंग्स रीसेट हो जाएँगी।",
            "প্রোগ্রামটি এখনও বন্ধ হয়নি। পরবর্তী শুরুতে সেটিংস রিসেট হবে।",
            "پروگرام ابھی بند نہیں ہوا ہے۔ اگلی بار شروع ہونے پر ترتیبات دوبارہ ترتیب دی جائیں گی۔",
            "程序尚未关闭。设置将在下次启动时重置。")

        Add("Не удалось запросить сброс настроек: {0}",
            "Could not request settings reset: {0}",
            "Не вдалося запитати скидання налаштувань: {0}",
            "Zurücksetzen der Einstellungen konnte nicht angefordert werden: {0}",
            "Impossibile richiedere il ripristino delle impostazioni: {0}",
            "No se pudo solicitar el restablecimiento de ajustes: {0}",
            "Impossible de demander la réinitialisation des paramètres : {0}",
            "Não foi possível solicitar a redefinição de configurações: {0}",
            "تعذّر طلب إعادة ضبط الإعدادات: {0}",
            "सेटिंग्स रीसेट का अनुरोध नहीं किया जा सका: {0}",
            "সেটিংস রিসেটের অনুরোধ করা যায়নি: {0}",
            "ترتیبات دوبارہ ترتیب دینے کی درخواست نہیں کی جا سکی: {0}",
            "未能请求重置设置：{0}")

        Add("Настройки сброшены к значениям по умолчанию. Прежние значения сохранены в файле: {0}. Чтобы вернуть их, закройте программу и откройте этот файл двойным щелчком.",
            "Settings were reset to defaults. Previous values are saved in the file: {0}. To restore them, close the application and open this file with a double-click.",
            "Налаштування скинуті до значень за замовчуванням. Попередні значення збережені у файлі: {0}. Щоб повернути їх, закрийте програму та відкрийте цей файл подвійним клацанням.",
            "Die Einstellungen wurden auf die Standardwerte zurückgesetzt. Bisherige Werte sind in der Datei gespeichert: {0}. Um sie wiederherzustellen, schließen Sie das Programm und öffnen Sie diese Datei per Doppelklick.",
            "Le impostazioni sono state reimpostate sui valori predefiniti. I valori precedenti sono salvati nel file: {0}. Per ripristinarli, chiudi il programma e apri questo file con un doppio clic.",
            "Los ajustes se restablecieron a los valores predeterminados. Los valores anteriores se guardaron en el archivo: {0}. Para restaurarlos, cierre la aplicación y abra este archivo con doble clic.",
            "Les paramètres ont été réinitialisés aux valeurs par défaut. Les valeurs précédentes sont enregistrées dans le fichier : {0}. Pour les restaurer, fermez l'application et ouvrez ce fichier d'un double-clic.",
            "As configurações foram redefinidas para os padrões. Os valores anteriores foram salvos no arquivo: {0}. Para restaurá-los, feche o programa e abra este arquivo com clique duplo.",
            "تمت إعادة ضبط الإعدادات إلى القيم الافتراضية. حُفظت القيم السابقة في الملف: {0}. لاستعادتها، أغلق البرنامج وافتح هذا الملف بالنقر المزدوج.",
            "सेटिंग्स डिफ़ॉल्ट मानों पर रीसेट हो गईं। पिछले मान फ़ाइल में सहेजे गए हैं: {0}। उन्हें वापस लाने के लिए, प्रोग्राम बंद करें और इस फ़ाइल को डबल-क्लिक करके खोलें।",
            "সেটিংস ডিফল্ট মানে রিসেট করা হয়েছে। পূর্ববর্তী মান ফাইলে সংরক্ষিত হয়েছে: {0}। সেগুলি পুনরুদ্ধার করতে, প্রোগ্রামটি বন্ধ করুন এবং ডাবল-ক্লিক করে এই ফাইলটি খুলুন।",
            "ترتیبات پہلے سے طے شدہ اقدار پر دوبارہ ترتیب دی گئیں۔ پچھلی اقدار فائل میں محفوظ ہیں: {0}۔ انہیں بحال کرنے کے لیے، پروگرام بند کریں اور اس فائل پر ڈبل کلک کریں۔",
            "设置已重置为默认值。先前的值已保存在文件中：{0}。若要恢复它们，请关闭程序并双击打开该文件。")

        Add("Настройки не сброшены: не удалось сохранить прежние значения ({0}). Всё осталось как было.",
            "Settings were not reset: could not save previous values ({0}). Everything remains unchanged.",
            "Налаштування не скинуті: не вдалося зберегти попередні значення ({0}). Усе залишилося як було.",
            "Einstellungen wurden nicht zurückgesetzt: Bisherige Werte konnten nicht gespeichert werden ({0}). Alles bleibt unverändert.",
            "Le impostazioni non sono state reimpostate: impossibile salvare i valori precedenti ({0}). È rimasto tutto invariato.",
            "Los ajustes no se restablecieron: no se pudieron guardar los valores anteriores ({0}). Todo permanece como estaba.",
            "Les paramètres n'ont pas été réinitialisés : impossible d'enregistrer les valeurs précédentes ({0}). Tout est resté inchangé.",
            "As configurações não foram redefinidas: não foi possível salvar os valores anteriores ({0}). Tudo permaneceu como estava.",
            "لم تتم إعادة ضبط الإعدادات: تعذّر حفظ القيم السابقة ({0}). بقي كل شيء كما كان.",
            "सेटिंग्स रीसेट नहीं हुईं: पिछले मान सहेजे नहीं जा सके ({0})। सब कुछ पहले जैसा ही रहा।",
            "সেটিংস রিসেট করা হয়নি: পূর্ববর্তী মান সংরক্ষণ করা যায়নি ({0})। সবকিছু আগের মতোই রয়েছে।",
            "ترتیبات دوبارہ ترتیب نہیں دی گئیں: پچھلی اقدار محفوظ نہیں ہو سکیں ({0})۔ سب کچھ ویسا ہی رہا جیسا تھا۔",
            "设置未重置：未能保存先前的值（{0}）。一切保持原样。")

        Add("Настройки сброшены не полностью: не удалось сбросить {0} из {1} значений. Прежние значения сохранены в файле: {2}. Отправьте журнал автору: «Настройки» - «О программе» - «Собрать и отправить».",
            "Settings were reset partially: could not reset {0} of {1} values. Previous values are saved in the file: {2}. Send the log to the author: Settings - About - Collect and send.",
            "Налаштування скинуті не повністю: не вдалося скинути {0} з {1} значень. Попередні значення збережені у файлі: {2}. Надішліть журнал автору: «Налаштування» - «Про програму» - «Зібрати та надіслати».",
            "Einstellungen wurden unvollständig zurückgesetzt: {0} von {1} Werten konnten nicht zurückgesetzt werden. Bisherige Werte sind in der Datei gespeichert: {2}. Senden Sie das Protokoll an den Autor: Einstellungen - Über das Programm - Sammeln und senden.",
            "Le impostazioni sono state reimpostate solo parzialmente: impossibile reimpostare {0} di {1} valori. I valori precedenti sono salvati nel file: {2}. Invia il registro all'autore: Impostazioni - Informazioni - Raccogli e invia.",
            "Los ajustes se restablecieron parcialmente: no se pudieron restablecer {0} de {1} valores. Los valores anteriores se guardaron en el archivo: {2}. Envíe el registro al autor: Ajustes - Acerca de - Recopilar y enviar.",
            "Les paramètres n'ont été que partiellement réinitialisés : impossible de réinitialiser {0} sur {1} valeurs. Les valeurs précédentes sont enregistrées dans le fichier : {2}. Envoyez le journal à l'auteur : Paramètres - À propos - Rassembler et envoyer.",
            "As configurações foram redefinidas parcialmente: não foi possível redefinir {0} de {1} valores. Os valores anteriores foram salvos no arquivo: {2}. Envie o log ao autor: Configurações - Sobre - Coletar e enviar.",
            "تمت إعادة ضبط الإعدادات جزئيًا: تعذّرت إعادة ضبط {0} من أصل {1} قيمة. حُفظت القيم السابقة في الملف: {2}. أرسل السجل إلى المؤلف: الإعدادات - حول البرنامج - جمع وإرسال.",
            "सेटिंग्स आंशिक रूप से रीसेट हुईं: {1} में से {0} मान रीसेट नहीं किए जा सके। पिछले मान फ़ाइल में सहेजे गए हैं: {2}। लेखक को लॉग भेजें: सेटिंग्स - के बारे में - एकत्र करें और भेजें।",
            "সেটিংস আংশিকভাবে রিসেট হয়েছে: {1} টির মধ্যে {0} টি মান রিসেট করা যায়নি। পূর্ববর্তী মান ফাইলে সংরক্ষিত হয়েছে: {2}। লেখককে লগ পাঠান: সেটিংস - পরিচিতি - সংগ্রহ করুন এবং পাঠান।",
            "ترتیبات جزوی طور پر دوبارہ ترتیب دی گئیں: {1} میں سے {0} اقدار دوبارہ ترتیب نہیں دی جا سکیں۔ پچھلی اقدار فائل میں محفوظ ہیں: {2}۔ مصنف کو لاگ بھیجیں: ترتیبات - تعارف - جمع کریں اور بھیجیں۔",
            "设置未完全重置：未能重置 {1} 个值中的 {0} 个。先前的值已保存在文件中：{2}。请将日志发送给作者：设置 - 关于 - 收集并发送。")

        Add("Запрос на сброс настроек устарел и не выполнен. Если он нужен, повторите его: «Настройки» - «О программе».",
            "The settings reset request has expired and was not performed. If still needed, repeat it: Settings - About.",
            "Запит на скидання налаштувань застарів і не виконаний. Якщо він потрібен, повторіть його: «Налаштування» - «Про програму».",
            "Die Anforderung zum Zurücksetzen der Einstellungen ist veraltet und wurde nicht ausgeführt. Bei Bedarf wiederholen: Einstellungen - Über das Programm.",
            "La richiesta di ripristino delle impostazioni è scaduta e non è stata eseguita. Se necessaria, ripeterla: Impostazioni - Informazioni.",
            "La solicitud de restablecimiento de ajustes ha caducado y no se ha realizado. Si la necesita, repítala: Ajustes - Acerca de.",
            "La demande de réinitialisation des paramètres a expiré et n'a pas été effectuée. Si nécessaire, renouvelez-la : Paramètres - À propos.",
            "A solicitação de redefinição de configurações expirou e não foi executada. Se ainda for necessária, repita-a: Configurações - Sobre.",
            "انتهت صلاحية طلب إعادة ضبط الإعدادات ولم يُنفذ. إذا كان مطلوبًا، كرره: الإعدادات - حول البرنامج.",
            "सेटिंग्स रीसेट का अनुरोध पुराना हो गया है और निष्पादित नहीं किया गया। यदि आवश्यक हो, तो इसे दोहराएँ: सेटिंग्स - के बारे में।",
            "সেটিংস রিসেটের অনুরোধের মেয়াদ শেষ হয়ে গেছে এবং কার্যকর করা হয়নি। প্রয়োজন হলে, এটি পুনরায় চেষ্টা করুন: সেটিংস - পরিচিতি।",
            "ترتیبات دوبارہ ترتیب دینے کی درخواست کی میعاد ختم ہو چکی ہے اور اس پر عمل نہیں کیا گیا۔ اگر ضرورت ہو تو اسے دہرائیں: ترتیبات - تعارف۔",
            "重置设置的请求已过期，未执行。如果需要，请重新尝试：设置 - 关于。")

    End Sub

End Class
