Option Strict On

' <summary>
' Strings of the settings import (103, 105 L12): the preview now names the policy that will
' really apply (merge as well as replace), and a profile that was applied but could not be
' written says so.
'
' Columns after the Russian key: en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
' Никогда не ставьте «умные» кавычки в литерал - VB считает U+201C/U+201D разделителями
' строки; используются «…» и 「…」.
' </summary>
Partial Public NotInheritable Class Localization

    Private Shared Sub AddSettingsImportStrings()

        Add("При объединении изменится параметров: {0}.",
            "Parameters that merging would change: {0}.",
            "Параметрів, які зміняться при об'єднанні: {0}.",
            "Parameter, die beim Zusammenführen geändert würden: {0}.",
            "Parametri che verrebbero modificati dall'unione: {0}.",
            "Parámetros que cambiaría la combinación: {0}.",
            "Paramètres que la fusion modifierait : {0}.",
            "Parâmetros que a mesclagem alteraria: {0}.",
            "عدد الإعدادات التي سيغيّرها الدمج: {0}.",
            "विलय से बदलने वाले पैरामीटर: {0}।",
            "একত্র করলে যে প্যারামিটারগুলি বদলাবে: {0}।",
            "ضم کرنے سے تبدیل ہونے والے پیرامیٹرز: {0}۔",
            "合并将更改的参数数量：{0}。")

        Add("Настройки применены, но записать их не удалось. Повторная попытка будет сделана автоматически. Резервная копия: {0}",
            "The settings were applied but could not be saved. A retry will be made automatically. Backup: {0}",
            "Налаштування застосовано, але записати їх не вдалося. Повторну спробу буде виконано автоматично. Резервна копія: {0}",
            "Die Einstellungen wurden angewendet, konnten aber nicht gespeichert werden. Ein weiterer Versuch erfolgt automatisch. Sicherung: {0}",
            "Le impostazioni sono state applicate ma non è stato possibile salvarle. Verrà eseguito un nuovo tentativo automatico. Backup: {0}",
            "Los ajustes se aplicaron, pero no se pudieron guardar. Se reintentará automáticamente. Copia de seguridad: {0}",
            "Les réglages ont été appliqués mais n'ont pas pu être enregistrés. Une nouvelle tentative sera faite automatiquement. Sauvegarde : {0}",
            "As configurações foram aplicadas, mas não foi possível salvá-las. Uma nova tentativa será feita automaticamente. Backup: {0}",
            "تم تطبيق الإعدادات لكن تعذّر حفظها. ستُجرى محاولة أخرى تلقائيًا. النسخة الاحتياطية: {0}",
            "सेटिंग्स लागू हो गईं, लेकिन सहेजी नहीं जा सकीं। दोबारा प्रयास अपने-आप किया जाएगा। बैकअप: {0}",
            "সেটিংস প্রয়োগ হয়েছে, কিন্তু সংরক্ষণ করা যায়নি। স্বয়ংক্রিয়ভাবে আবার চেষ্টা করা হবে। ব্যাকআপ: {0}",
            "ترتیبات لاگو ہو گئیں لیکن محفوظ نہ ہو سکیں۔ خودکار طور پر دوبارہ کوشش کی جائے گی۔ بیک اپ: {0}",
            "设置已应用，但未能保存。将自动重试。备份：{0}")

    End Sub

End Class
