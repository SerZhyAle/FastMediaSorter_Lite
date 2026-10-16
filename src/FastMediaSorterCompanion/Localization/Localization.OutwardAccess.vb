Option Strict On
' Columns: Russian key, en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh.
Partial Public NotInheritable Class Localization
    Private Shared Sub AddOutwardAccessStrings()
        ' Ticket 239 CR-13 (Q239-2): the guest invitation and the link with a password are shown as not available. The one line
        ' says the true cause (the file server cannot yet limit a login to one folder) and promises no date.
        Add("Недоступно: файловый сервер программы пока не умеет ограничить вход одной папкой.",
            "Not available: the program's file server cannot yet limit a login to one folder.",
            "Недоступно: файловий сервер програми поки не вміє обмежити вхід однією папкою.",
            "Nicht verfügbar: Der Dateiserver des Programms kann einen Login noch nicht auf einen Ordner beschränken.",
            "Non disponibile: il file server del programma non sa ancora limitare un accesso a una sola cartella.",
            "No disponible: el servidor de archivos del programa aún no puede limitar un inicio de sesión a una sola carpeta.",
            "Indisponible : le serveur de fichiers du programme ne sait pas encore limiter une connexion à un seul dossier.",
            "Indisponível: o servidor de ficheiros do programa ainda não consegue limitar um início de sessão a uma só pasta.",
            "غير متاح: خادم الملفات في البرنامج لا يستطيع بعد حصر تسجيل الدخول في مجلد واحد.",
            "उपलब्ध नहीं: प्रोग्राम का फ़ाइल सर्वर अभी किसी लॉगिन को एक फ़ोल्डर तक सीमित नहीं कर सकता।",
            "অনুপলব্ধ: প্রোগ্রামের ফাইল সার্ভার এখনও একটি লগইনকে একটি ফোল্ডারে সীমাবদ্ধ করতে পারে না।",
            "دستیاب نہیں: پروگرام کا فائل سرور ابھی کسی لاگ اِن کو ایک فولڈر تک محدود نہیں کر سکتا۔",
            "不可用：程序的文件服务器目前还不能把登录限制在单个文件夹内。")

        ' The group title of the guest invitation: the old title said "for this folder", which the server does not enforce.
        Add("Гостевое приглашение",
            "Guest invitation", "Гостьове запрошення", "Gasteinladung", "Invito per ospite",
            "Invitación de invitado", "Invitation d'invité", "Convite de convidado",
            "دعوة ضيف", "अतिथि आमंत्रण", "অতিথি আমন্ত্রণ",
            "مہمان کی دعوت", "访客邀请")
    End Sub
End Class
