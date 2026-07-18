# Тактическая спецификация С5: зум и панорама

Статус: реализовано 2026-07-16 (обе сборки)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Смежная: [SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md](SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md) (эта стадия реализует её инварианты, а не пересматривает модель)
Сборки: **обе** (Б-33 - только modern)
Объём: 3 бага, 2 файла

## 0. Цель

Довести незакоммиченный зум-слой ([src/Main_Form.Zoom.vb](../../src/Main_Form.Zoom.vb),
work in progress) до состояния, в котором он не противоречит собственной спеке, и
починить историческую панораму, которая все 15 лет ехала вдвое медленнее мыши.

| Ид | П | Сборки | Что |
|---|---|---|---|
| Б-33 | П2 | modern | Зум не сбрасывается при смене файла - метка и масштаб врут, клик перестаёт листать |
| Б-34 | П3 | обе | Панорама идёт с половинной скоростью и дёргается |
| Б-35 | П3 | обе | Потерянный MouseUp: телепорт бокса при повторном захвате |

## 1. Б-33: Fit на каждом новом файле

Инвариант 8 зум-спеки: «Fit пересчитывается при загрузке файла». В пути показа его
не выполняет никто (`grep SkipZoom|ZoomToFit src/` → только `ISizeChanged`,
Alt-ветка `MouseUse`, клик по `lbl_Zoom`). Последствия: после 200 % на 4000x3000
следующий файл 800x600 показан в ~1000 % с меткой «200 %»; `zoom_Scale = 0`
отключает клик-навигацию **для всех последующих файлов**; `Draw_Perspective`
строит фон под бокс 8000x6000 (~190 МБ и секунды `GetPixel` на каждый переход).

Точка вставки - `LoadStandardImageInPictureBox`, ветка «файл действительно
меняется» ([MediaLoading.vb:456](../../src/Main_Form.MediaLoading.vb)), **до** показа:

```vb
If current_Loaded_File_Name <> Current_File_Name Then
#If Not NETFRAMEWORK Then
    ' Инвариант 8 зум-спеки: каждый новый файл открывается вписанным. Без этого
    ' геометрия боксов остаётся от прошлого изображения: метка врёт, клик не
    ' листает (zoom_Scale = 0), а Draw_Perspective строит фон под зумленный бокс.
    If zoom_Scale <> 1 Then ZoomToFit()
#End If
```

`ZoomToFit` уже делегирует в `SkipZoom()` (единственное определение «вписать») и
приводит `zoom_Factor`/метку в согласованное состояние.

**net48 не трогаем**: там историческая механика зума заморожена спекой (зум держится
между файлами, `zoom_Scale` хранит сырое произведение). Отсюда шов.

## 2. Б-34: панорама в неподвижной системе координат

[Main_Form.MouseInput.vb:337-350](../../src/Main_Form.MouseInput.vb). `e.X`/`e.Y` -
клиентские координаты **самого PictureBox**, а бокс сдвигается на каждом обновлении:
система отсчёта едет вместе с объектом. Применённое смещение подчиняется
`a(n) = D(n) - a(n-1)` → картинка следует за рукой с половинной скоростью, каждое
второе событие её не двигает (статтер), точка захвата уезжает из-под курсора.

Считаем от `panel_Media` - он неподвижен:

```vb
' Поля вместо original_PictureBox_Left/Top
Private drag_Grab_Offset As Size   ' курсор минус левый-верхний угол бокса, в координатах panel_Media
```

Старт перетаскивания:
```vb
is_Dragging = True
Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
drag_Grab_Offset = New Size(cursor_On_Panel.X - Picture_Box_1.Left, cursor_On_Panel.Y - Picture_Box_1.Top)
```

Движение:
```vb
' Дельта считается от panel_Media (неподвижен), а НЕ от бокса: клиентские
' координаты движущегося бокса давали половинную скорость и дрожание (Б-34).
Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
Dim new_Left As Integer = cursor_On_Panel.X - drag_Grab_Offset.Width
Dim new_Top As Integer = cursor_On_Panel.Y - drag_Grab_Offset.Height
Picture_Box_1.Location = New Point(new_Left, new_Top)
Picture_Box_2.Location = Picture_Box_1.Location
```

Порог начала перетаскивания (5 px) и троттлинг `DRAG_UPDATE_INTERVAL_MS` (16 мс)
сохраняются как есть. Правка общая для обеих сборок: это исправление явного дефекта,
а не смена механики (жест тот же - «тащить изображение мышью»).

Кламп панорамы (изображение нельзя увести целиком за пределы панели) - **не здесь**:
это У-14, стадия [С10](SPECIFICATION_VIEWER_CORE_S10_MODERN_UX.md), modern-only.

## 3. Б-35: конец перетаскивания и зум во время удержания

Состояние drag завершается только лениво - следующим `MouseMove` без кнопки.
Два отказа: (1) отпустить ЛКМ, не двигая мышь, и нажать снова → `is_Dragging` всё
ещё True, база от прошлого drag → бокс телепортируется; (2) зум колесом при зажатой
ЛКМ → `ApplyZoomFactor` ставит `Bounds`, а следующий `MouseMove` возвращает бокс к
устаревшей базе, отбрасывая якорь зума.

1. Обработчики на оба бокса (сейчас `MouseUp` у них нет вовсе):
   ```vb
   Private Sub Picture_Box_MouseUp(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseUp, Picture_Box_2.MouseUp
       EndDrag()
   End Sub
   Private Sub Picture_Box_MouseLeave(sender As Object, e As EventArgs) Handles Picture_Box_1.MouseLeave, Picture_Box_2.MouseLeave
       EndDrag()
   End Sub
   Private Sub EndDrag()
       If Not is_Dragging Then Return
       is_Dragging = False
       last_Drag_Update_Time = DateTime.MinValue
   End Sub
   ```
2. `ApplyZoomFactor` ([Zoom.vb:105-106](../../src/Main_Form.Zoom.vb)) после установки
   `Bounds` переинициализирует базу, если тащат прямо сейчас:
   ```vb
   If is_Dragging Then
       Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
       drag_Grab_Offset = New Size(cursor_On_Panel.X - bounds.Left, cursor_On_Panel.Y - bounds.Top)
   End If
   ```
   (в net48 то же - в Ctrl+wheel-ветке `MouseUse`.)

## 4. Приёмка

1. **Fit на новом файле** (modern): открыть 4000x3000, Ctrl+колесо до 200 % → листнуть
   на 800x600 → изображение вписано, метка «Вписать N %», левый клик снова листает,
   переход мгновенный (нет секундной паузы на `Draw_Perspective`).
2. **Fit после зума и полноэкранки**: 300 % → F7 → выход → следующий файл - вписан.
3. **Панорама 1:1**: зум 400 %, тащить изображение → точка под курсором остаётся под
   курсором на всём протяжении жеста, без дрожи и «отставания вдвое». Проверить в
   обеих сборках.
4. **Повторный захват**: тащить, отпустить ЛКМ **не двигая мышь**, нажать снова и
   потащить → изображение продолжает движение с текущего места (без телепорта).
5. **Зум во время удержания**: держа ЛКМ, крутить колесо (modern: Ctrl+колесо или
   режим «колесо зумит») → изображение зумится вокруг курсора и не прыгает при
   следующем движении мыши.
6. **net48 не изменился**: зум держится между файлами (историческое поведение),
   Ctrl+колесо и Alt+колесо работают как раньше.

## 5. Риски

- [Main_Form.Zoom.vb](../../src/Main_Form.Zoom.vb) **не закоммичен** - стадия должна
  идти после его коммита, иначе диффы перепутаются.
- Б-34 меняет ощущение от панорамы в x86 (станет вдвое «быстрее» - т.е. корректно).
  Это исправление, но владельцу стоит взглянуть - привычка могла закрепиться.
- `MouseLeave` → `EndDrag` может обрывать перетаскивание при быстром выходе курсора
  за край бокса; если мешает - оставить только `MouseUp` + `Capture`.

## 6. Готово, когда

- [ ] 3 бага закрыты; `grep original_PictureBox_Left src/` пуст.
- [ ] `.\build.ps1`; приёмка 1-6 (1-2 - modern, 3-5 - обе, 6 - x86).
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
