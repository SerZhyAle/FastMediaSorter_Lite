# Промпт: публикация Win32-приложения в Microsoft Store (MSIX, путь A)

Переиспользуемый промпт, собранный из того, что уже применено в проекте **FastMediaSorter LITE**.
Вставь блок ниже в другой свой проект (того же издателя **SZA**) - и приложение пройдёт тот же путь
без повторного выяснения общих параметров.

## Что переносится между проектами (мой аккаунт-издатель SZA), а что нет

| Значение | Откуда | Переносится? |
| --- | --- | --- |
| `Package/Identity/Publisher` = `CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD` | привязан к аккаунту | **ДА** - один и тот же для всех моих продуктов |
| `Package/Properties/PublisherDisplayName` = `SZA` | привязан к аккаунту | **ДА** - один и тот же |
| `Package/Identity/Name` (`-IdentityName`) | резервируется на каждый продукт | **НЕТ** - своё имя для нового приложения (Partner Center > New product > reserve name > Product identity) |
| IARC `Global Rating ID` = `7d9b315a-f211-8505-80d0-3f4bee633770` | привязан к анкете FastMediaSorter | **частично** - вставляй тот же ID, только если функционал нового приложения не меняет ответы анкеты; иначе пройди короткую анкету заново |

Итог: `-Publisher` и `-PublisherDisplayName` уже известны и зашиты ниже. Для нового приложения
нужно только зарезервировать имя и подставить `-IdentityName`.

---

## Промпт (копировать целиком)

````
Опубликуй это приложение в Microsoft Store через MSIX (путь A: full-trust desktop MSIX).
Воспроизведи схему, которую я уже применил в проекте FastMediaSorter LITE. Действуй по фазам.

=== МОИ ПОСТОЯННЫЕ ИДЕНТИФИКАТОРЫ ИЗДАТЕЛЯ (аккаунт SZA, переносятся между проектами) ===
- Publisher (Package/Identity/Publisher):  CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD
- PublisherDisplayName:                    SZA
- IARC Global Rating ID (портативный):     7d9b315a-f211-8505-80d0-3f4bee633770
ЭТИ ДВА (Publisher, PublisherDisplayName) - одинаковы для всех моих продуктов, не выясняй заново,
подставляй их в build-msix.ps1 как -Publisher и -PublisherDisplayName по умолчанию.
IdentityName (-IdentityName) - СВОЙ для этого приложения: зарезервируй имя в Partner Center
(New product > MSIX or PWA app > reserve name) и возьми Package/Identity/Name из Product identity.
IARC Global Rating ID вставляй тот же, только если функционал не меняет ответы анкеты; иначе пройди анкету.

=== ПОЧЕМУ ИМЕННО ЭТОТ ПУТЬ ===
- Аккаунт разработчика бесплатный (физлица с конца 2025, компании с мая 2026).
- Microsoft ПЕРЕподписывает MSIX при сертификации - платный code-signing сертификат НЕ нужен.
  (Альтернативный путь "unpackaged exe/MSI" сертификат требует.)
- Store-подпись + Store-дистрибуция лучше всего гасит ложные срабатывания антивирусов.
- Это ДОПОЛНЕНИЕ к существующим каналам (GitHub release, winget) - их не трогаем.

=== ФАЗА 1. Проверить, что приложение MSIX-совместимо (код) ===
MSIX запускает desktop-приложение в лёгком контейнере: каталог установки ТОЛЬКО ДЛЯ ЧТЕНИЯ,
а %LOCALAPPDATA% и HKCU виртуализируются per-package. Проверь по чеклисту и почини, если надо:
- Любая запись рядом с .exe (в каталог установки) сломается -> переноси в %LOCALAPPDATA%\<Vendor>\<App>.
- Настройки: HKCU виртуализируется - ок, если их не нужно читать снаружи пакета.
- Скачиваемые данные/кеш/логи: писать только в %LOCALAPPDATA%, не рядом с exe.
- File-associations: в MSIX нельзя писать HKCU\Software\Classes - объявляй их в манифесте
  как windows.fileTypeAssociation.
- Win32 API (WebBrowser/медиа/GDI+/файлы/локальный HTTP): требуют runFullTrust (см. манифест).
Правило: всё изменяемое -> %LOCALAPPDATA% или реестр; рядом с exe только чтение.
Если приложение уже так устроено - правок кода НЕ требуется (тот же exe идёт и packaged, и unpackaged).

=== ФАЗА 2. Артефакты упаковки (создать в репозитории) ===
Создай каталог msix/ со следующим:
1) msix/AppxManifest.xml - манифест с плейсхолдерами __IDENTITY_NAME__, __PUBLISHER__,
   __PUBLISHER_DISPLAY__, __VERSION__. Ключевое:
   - <Identity Name Publisher Version ProcessorArchitecture="x64">
   - <Properties>: DisplayName, PublisherDisplayName, Logo=Assets\StoreLogo.png
   - <Dependencies>: TargetDeviceFamily Windows.Desktop MinVersion 10.0.17763.0 (1809 - безопасный пол)
   - <Application EntryPoint="Windows.FullTrustApplication" Executable="<App>.exe">
     + uap:VisualElements (Square150/44, DefaultTile Square71), BackgroundColor
     + Extensions: uap:FileTypeAssociation с нужными расширениями (если приложение их открывает)
   - <Capabilities>: <rescap:Capability Name="runFullTrust"/>  (единственная capability)
   - Namespaces: foundation/windows10, uap, rescap; IgnorableNamespaces="uap rescap"
   ВАЖНО: не добавляй Square310x310Logo без парного Wide310x150Logo (иначе ошибка).
   Достаточно плиток 44/71/150 + StoreLogo(50).
2) msix/build-msix.ps1 - скрипт, который:
   a) Собирает Release через MSBuild (vswhere для поиска MSBuild; -NoBuild чтобы переиспользовать).
   b) Версия: Store требует 4-значную с РЕВИЗИЕЙ=0 (Major.Minor.Build.0), каждая часть <= 65535.
      Если exe штампуется как YY.M.D.HHmm -> ремап в YY.(M*100+D).HHmm.0 (монотонно, уникально по минутам).
      НЕ редактировать вручную.
   c) Стейджит оффлайн-payload (exe + все рантайм-зависимости, БЕЗ *.pdb/*.xml) в msix/stage.
   d) Генерирует логотипы из мастера assets/icons/store-icon-256.png (HighQualityBicubic):
      Square44x44Logo(44), StoreLogo(50), Square71x71Logo(71), Square150x150Logo(150) -> stage/Assets.
   e) Подставляет плейсхолдеры в AppxManifest.xml, кладёт его в КОРЕНЬ stage.
   f) makeappx pack /d stage /p dist/<App>-<version>-x64.msix /o
   g) Параметры (значения издателя по умолчанию уже мои):
      -IdentityName (СВОЁ для приложения)
      -Publisher            = 'CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD'   (default)
      -PublisherDisplayName = 'SZA'                                       (default)
      -Configuration -NoBuild -SelfSign [+ доменные флаги payload при необходимости].
   h) -SelfSign: New-SelfSignedCertificate (Subject == -Publisher!), signtool sign /fd SHA256,
      Export-Certificate, и ВЫВЕСТИ команды: Import-Certificate (как админ, в
      Cert:\LocalMachine\TrustedPeople) + Add-AppxPackage.
   Поиск SDK-тулзов: makeappx.exe/signtool.exe в "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\".
3) msix/README.md - инструкции сборки/сабмита.
4) assets/icons/store-icon-256.png - мастер-логотип 256px.
5) tools/store/make-screenshot.ps1 - скриншот >= 1366x768 PNG.
6) docs/privacy.html - страница политики приватности (хостить на GitHub Pages -> URL для листинга).
7) Добавь msix/stage и msix/dist в .gitignore.

Инструменты: winget install Microsoft.WindowsSDK (makeappx+signtool) и Visual Studio 2022 / MSBuild.

=== ФАЗА 3. Локальная проверка ДО загрузки ===
cd msix; .\build-msix.ps1 -SelfSign   (можно -NoBuild для текущей сборки)
Затем: доверить выведенный сертификат (как админ) и Add-AppxPackage <msix>.
Прогнать ключевые сценарии приложения; проверить установку "по умолчанию" в
Параметры > Приложения > Приложения по умолчанию (если есть file-associations).
Подводные камни:
- Add-AppxPackage УСТАНАВЛИВАЕТ, но НЕ запускает - стартуй из меню Пуск.
- Square310 без Wide310 -> ошибка упаковки.
- Если приложение single-instance по mutex - закрой dev/Release копии при тесте пакета.

=== ФАЗА 4. Partner Center: аккаунт + идентичность ===
1) Account settings > Programs > Windows > Get started (НЕ "Windows Desktop Applications" -
   то телеметрия для EV-подписанных Win32). Аккаунт у меня уже есть (издатель SZA).
2) Apps and games > New product > MSIX or PWA app -> зарезервировать имя приложения.
3) Product > Product identity -> проверить значения:
   - Package/Identity/Name           -> -IdentityName  (новое, для этого приложения)
   - Package/Identity/Publisher      -> должно быть CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD (моё)
   - Package/Properties/PublisherDisplayName -> должно быть SZA (моё)
   Они должны совпадать ТОЧНО, иначе аплоад отклонят.
4) Собрать Store-пакет БЕЗ -SelfSign и загрузить UNSIGNED .msix (Microsoft подпишет сам):
   .\build-msix.ps1 -IdentityName "<новое Name>"
   (-Publisher и -PublisherDisplayName уже зашиты как мои значения по умолчанию)

=== ФАЗА 5. Материалы листинга ===
- Privacy policy: обязательна (если читает локальные файлы / делает сетевые вызовы). URL на GitHub Pages.
- Screenshots: минимум 1, PNG >= 1366x768.
- Store logos: опциональны (Store берёт плитки из пакета).
- Description: обязательно (текст в стиле проекта).
- Product features: список, каждая строка <= 200 символов.
- Pricing: "Free" = выбрать в выпадающем Retail price.
- runFullTrust justification: обязательна для КАЖДОГО desktop MSIX, лимит ~1000 символов.
  Объяснить: это full-trust Win32 (не UWP), перечислить какие Win32 API и зачем, "no telemetry".
- Age rating: короткая анкета -> IARC генерирует рейтинг.
  Можно вставить мой портативный Global Rating ID 7d9b315a-f211-8505-80d0-3f4bee633770,
  только если функционал нового приложения не меняет ответы анкеты; иначе пройди анкету заново.
  Re-rating нужен только при изменениях, меняющих ответы (IAP, реклама, UGC-шаринг).

=== ФАЗА 6. Сабмит -> сертификация (несколько рабочих дней) ===
Если есть опциональные сетевые вызовы (к настраиваемому пользователем endpoint) - прямо описать
это в description + privacy policy, чтобы снять вопросы ревью о сетевой активности.

=== СТИЛЬ ТЕКСТОВ ===
Дефис, не em-dash; ".." вместо "..."; ё в русском. Применять к docs/UI/комментариям/листингу.

Адаптируй имена (<App>, <Vendor>, расширения, путь %LOCALAPPDATA%) под текущий проект.
Сначала покажи мне план изменений, потом создавай файлы.
````

---

## Откуда взяты значения (для самопроверки)

- `-Publisher` / `-PublisherDisplayName` по умолчанию - из [../../msix/build-msix.ps1](../../msix/build-msix.ps1) (параметры `$Publisher`, `$PublisherDisplayName`).
- IARC Global Rating ID - из [../../STORE_PUBLISHING.md](../../STORE_PUBLISHING.md) (раздел "Age rating (IARC)").
- Полный исходный плейбук этого проекта - [../../STORE_PUBLISHING.md](../../STORE_PUBLISHING.md) и [../../msix/README.md](../../msix/README.md).
