Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Namespace PalmDesktopHarness
    Friend NotInheritable Class LcdPanel
        Inherits Control

        Public Enum DisplayRenderMode
            NormalMono
            InvertedGreenBacklight
        End Enum

        Public Enum DisplayLayoutMode
            PalmHandheld
            Esp32Board
        End Enum

        Private Const BoardWidth As Integer = 480
        Private Const BoardHeight As Integer = 272
        Private Const BoardRotateCounterClockwise As Boolean = True
        Private Const BoardPalmViewX As Integer = 53
        Private Const BoardPalmViewY As Integer = 0
        Private Const BoardPalmViewW As Integer = 374
        Private Const BoardPalmViewH As Integer = 272
        Private Const BoardLcdX As Integer = 155
        Private Const BoardLcdY As Integer = 0
        Private Const BoardLcdW As Integer = 272
        Private Const BoardLcdH As Integer = 272
        Private Const BoardLeftButtonX1 As Integer = 43
        Private Const BoardRightButtonX0 As Integer = 437
        Private Const BoardButtonCount As Integer = 6
        Private Const KeyBitPower As UShort = &H1US
        Private Const KeyBitPageUp As UShort = &H2US
        Private Const KeyBitPageDown As UShort = &H4US
        Private Const KeyBitHard1 As UShort = &H8US
        Private Const KeyBitHard2 As UShort = &H10US
        Private Const KeyBitHard3 As UShort = &H20US
        Private Const KeyBitHard4 As UShort = &H40US
        Private Const KeyBitResetOs As UShort = &H2000US
        Private Const KeyBitSaveState As UShort = &H4000US

        Private frameBytes As Byte()
        Private frameWidth As Integer
        Private frameHeight As Integer
        Private framePitch As Integer
        Private frameBpp As Integer = 1
        Private framePan As Integer
        Private frameContrast As UShort
        Private framePalette As UInteger()
        Private displayMode As DisplayRenderMode = DisplayRenderMode.NormalMono
        Private layoutModeValue As DisplayLayoutMode = DisplayLayoutMode.PalmHandheld
        Private lastPoint As Point
        Private isDisplayAsleep As Boolean
        Private activeBoardButtonBits As UShort
        Private activeBoardButtonLabel As String = ""
        Private boardPenActive As Boolean
        Private boardPaintingWithTransform As Boolean
        Private Shared ReadOnly boardImages As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)

        Public Event PenChanged(down As Boolean, x As Integer, y As Integer)
        Public Event ButtonChanged(bits As UShort, down As Boolean, label As String)

        Public Sub New()
            DoubleBuffered = True
            BackColor = Color.White
            Size = New Size(PalmConfig.DigitizerWidth * 2, PalmConfig.DigitizerHeight * 2)
            MinimumSize = Size
        End Sub

        Public Sub UpdateFrame(bytes As Byte(), width As Integer, height As Integer, pitch As Integer, bpp As Integer, pan As Integer, Optional palette As UInteger() = Nothing)
            frameBytes = bytes
            frameWidth = width
            frameHeight = height
            framePitch = pitch
            frameBpp = bpp
            framePan = pan
            framePalette = palette
            Invalidate()
        End Sub

        Public Property DisplayAsleep As Boolean
            Get
                Return isDisplayAsleep
            End Get
            Set(value As Boolean)
                If isDisplayAsleep = value Then Return
                isDisplayAsleep = value
                Invalidate()
            End Set
        End Property

        Public Property ContrastValue As UShort
            Get
                Return frameContrast
            End Get
            Set(value As UShort)
                If frameContrast = value Then Return
                frameContrast = value
                Invalidate()
            End Set
        End Property

        Public Property RenderMode As DisplayRenderMode
            Get
                Return displayMode
            End Get
            Set(value As DisplayRenderMode)
                If displayMode = value Then Return
                displayMode = value
                Invalidate()
            End Set
        End Property

        Public Property LayoutMode As DisplayLayoutMode
            Get
                Return layoutModeValue
            End Get
            Set(value As DisplayLayoutMode)
                If layoutModeValue = value Then Return
                ReleaseBoardButton()
                boardPenActive = False
                Capture = False
                layoutModeValue = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)

            If layoutModeValue = DisplayLayoutMode.Esp32Board Then
                PaintEsp32Board(e.Graphics)
                Return
            End If

            Dim scaleX = ClientSize.Width / CSng(PalmConfig.DigitizerWidth)
            Dim scaleY = ClientSize.Height / CSng(PalmConfig.DigitizerHeight)
            Dim lcdScaleY = scaleY
            Dim silkscreenTop = PalmConfig.LcdHeight * scaleY
            Dim lcdRect As New RectangleF(0, 0, PalmConfig.LcdWidth * scaleX, PalmConfig.LcdHeight * lcdScaleY)

            e.Graphics.Clear(PageBackgroundColor())
            e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor
            e.Graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half

            If isDisplayAsleep Then
                Using bg As New SolidBrush(SleepLcdBackgroundColor())
                    e.Graphics.FillRectangle(bg, lcdRect)
                End Using
            ElseIf frameBytes IsNot Nothing AndAlso frameWidth > 0 AndAlso frameHeight > 0 AndAlso framePitch > 0 Then
                Dim drawWidth = Math.Min(PalmConfig.LcdWidth, frameWidth)
                Dim drawHeight = Math.Min(PalmConfig.LcdHeight, frameHeight)
                Dim maxValue = Math.Max(1, (1 << Math.Min(frameBpp, 8)) - 1)
                Dim brushes(maxValue) As SolidBrush

                Try
                    Using bg As New SolidBrush(LcdBackgroundColor())
                        e.Graphics.FillRectangle(bg, lcdRect)
                    End Using

                    Dim firstBrush = If(UseFramePalette(), 0, 1)
                    For i = firstBrush To maxValue
                        brushes(i) = New SolidBrush(PixelColor(i, maxValue))
                    Next

                    For y = 0 To drawHeight - 1
                        Dim row = y * framePitch
                        For x = 0 To drawWidth - 1
                            Dim value = GetPixelValue(row, x + framePan)
                            If value > 0 OrElse UseFramePalette() Then
                                e.Graphics.FillRectangle(brushes(value), x * scaleX, y * lcdScaleY, Math.Max(1.0F, scaleX), Math.Max(1.0F, lcdScaleY))
                            End If
                        Next
                    Next
                Finally
                    For Each brush In brushes
                        brush?.Dispose()
                    Next
                End Try
            End If

            DrawSilkscreen(e.Graphics, scaleX, scaleY, silkscreenTop, isDisplayAsleep)

        End Sub

        Private Sub PaintEsp32Board(g As Graphics)
            g.Clear(SystemColors.Control)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.InterpolationMode = InterpolationMode.NearestNeighbor
            g.PixelOffsetMode = PixelOffsetMode.Half

            Dim boardRect = BoardDisplayRectangle()
            Using bg As New SolidBrush(Color.FromArgb(224, 224, 224))
                g.FillRectangle(bg, boardRect)
            End Using

            Dim state = g.Save()
            boardPaintingWithTransform = True
            Try
                ApplyBoardTransform(g, boardRect)

                Using boardBrush As New SolidBrush(Color.Black)
                    g.FillRectangle(boardBrush, New RectangleF(0, 0, BoardWidth, BoardHeight))
                End Using

                DrawBoardButtons(g)
                DrawBoardSilkscreen(g)
                DrawBoardLcd(g)
            Finally
                boardPaintingWithTransform = False
                g.Restore(state)
            End Try
        End Sub

        Private Function BoardDisplayRectangle() As RectangleF
            Dim displayWidth = If(BoardRotateCounterClockwise, BoardHeight, BoardWidth)
            Dim displayHeight = If(BoardRotateCounterClockwise, BoardWidth, BoardHeight)
            Dim scale = Math.Min(ClientSize.Width / CSng(displayWidth), ClientSize.Height / CSng(displayHeight))
            If scale <= 0.0F Then scale = 1.0F
            Dim w = displayWidth * scale
            Dim h = displayHeight * scale
            Return New RectangleF((ClientSize.Width - w) / 2.0F, (ClientSize.Height - h) / 2.0F, w, h)
        End Function

        Private Sub ApplyBoardTransform(g As Graphics, boardRect As RectangleF)
            Dim scale = If(BoardRotateCounterClockwise, boardRect.Width / BoardHeight, boardRect.Width / BoardWidth)
            g.TranslateTransform(boardRect.Left, boardRect.Top)
            If BoardRotateCounterClockwise Then
                g.TranslateTransform(0.0F, BoardWidth * scale)
                g.ScaleTransform(scale, scale)
                g.RotateTransform(-90.0F)
            Else
                g.ScaleTransform(scale, scale)
            End If
        End Sub

        Private Function BoardToClient(rect As RectangleF) As RectangleF
            If boardPaintingWithTransform Then Return rect

            Dim boardRect = BoardDisplayRectangle()
            Dim scale = boardRect.Width / BoardWidth
            Return New RectangleF(boardRect.Left + rect.Left * scale,
                                  boardRect.Top + rect.Top * scale,
                                  rect.Width * scale,
                                  rect.Height * scale)
        End Function

        Private Function BoardToClientPoint(x As Single, y As Single) As PointF
            If boardPaintingWithTransform Then Return New PointF(x, y)

            Dim boardRect = BoardDisplayRectangle()
            Dim scale = boardRect.Width / BoardWidth
            Return New PointF(boardRect.Left + x * scale, boardRect.Top + y * scale)
        End Function

        Private Function TryClientToBoard(point As Point, ByRef x As Single, ByRef y As Single) As Boolean
            Dim boardRect = BoardDisplayRectangle()
            If Not boardRect.Contains(point.X, point.Y) Then Return False

            If BoardRotateCounterClockwise Then
                Dim scale = boardRect.Width / BoardHeight
                Dim localX = (point.X - boardRect.Left) / scale
                Dim localY = (point.Y - boardRect.Top) / scale
                x = BoardWidth - localY
                y = localX
                x = Math.Max(0.0F, Math.Min(BoardWidth - 0.001F, x))
                y = Math.Max(0.0F, Math.Min(BoardHeight - 0.001F, y))
                Return True
            End If

            x = (point.X - boardRect.Left) * BoardWidth / boardRect.Width
            y = (point.Y - boardRect.Top) * BoardHeight / boardRect.Height
            Return True
        End Function

        Private Sub DrawBoardButtons(g As Graphics)
            DrawButtonStrip(g, True)
            DrawButtonStrip(g, False)
        End Sub

        Private Sub DrawButtonStrip(g As Graphics, leftSide As Boolean)
            Dim stripX = If(leftSide, 0, BoardRightButtonX0)
            Dim stripW = If(leftSide, BoardLeftButtonX1, BoardWidth - BoardRightButtonX0)

            Using stripBrush As New SolidBrush(Color.Black)
                g.FillRectangle(stripBrush, BoardToClient(New RectangleF(stripX, 0, stripW, BoardHeight)))
            End Using

            If leftSide Then
                For i = 0 To BoardButtonCount - 1
                    DrawSingleBoardButton(g,
                                          New RectangleF(CSng(stripX + 3), CSng((BoardHeight * i) / CDbl(BoardButtonCount) + 3), CSng(stripW - 6), BoardHeight / CSng(BoardButtonCount) - 6.0F),
                                          BoardButtonLabelForLeftIndex(i),
                                          BoardButtonIconForLeftIndex(i),
                                          activeBoardButtonBits = BoardButtonBitsForLeftIndex(i))
                Next
                Return
            End If

            Dim powerTop = CSng((BoardHeight * 0) / CDbl(BoardButtonCount))
            Dim powerBottom = CSng((BoardHeight * 4) / CDbl(BoardButtonCount))
            DrawSingleBoardButton(g, New RectangleF(CSng(stripX + 3), powerTop + 3.0F, CSng(stripW - 6), powerBottom - powerTop - 6.0F), "PWR", "Btn_Power.png", activeBoardButtonBits = KeyBitPower)
            DrawSingleBoardButton(g, New RectangleF(CSng(stripX + 3), CSng((BoardHeight * 4) / CDbl(BoardButtonCount) + 3), CSng(stripW - 6), BoardHeight / CSng(BoardButtonCount) - 6.0F), "SAV", "Btn_Save.png", activeBoardButtonBits = KeyBitSaveState)
            DrawSingleBoardButton(g, New RectangleF(CSng(stripX + 3), CSng((BoardHeight * 5) / CDbl(BoardButtonCount) + 3), CSng(stripW - 6), BoardHeight / CSng(BoardButtonCount) - 6.0F), "RST", "Btn_Reset.png", activeBoardButtonBits = KeyBitResetOs)
        End Sub

        Private Sub DrawSingleBoardButton(g As Graphics, logicalRect As RectangleF, label As String, iconName As String, down As Boolean)
            Dim rect = Rectangle.Round(BoardToClient(logicalRect))
            Using path = RoundedPath(rect, Math.Max(4, CInt(rect.Height * 0.14F)))
                Using fill As New SolidBrush(If(down, Color.FromArgb(72, 72, 72), Color.FromArgb(48, 48, 48)))
                    g.FillPath(fill, path)
                End Using
                Using pen As New Pen(Color.FromArgb(96, 96, 96), If(down, 2.0F, 1.0F))
                    g.DrawPath(pen, path)
                End Using
            End Using

            Dim icon = LoadBoardImage(iconName)
            If icon IsNot Nothing Then
                Dim imageRect = CenteredIconRect(logicalRect, icon)
                g.DrawImage(icon, BoardToClient(imageRect))
                Return
            End If

            Using font As New Font(FontFamily.GenericSansSerif, Math.Max(6.0F, rect.Height * 0.22F), FontStyle.Bold),
                  brush As New SolidBrush(Color.FromArgb(96, 96, 96)),
                  format As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                g.DrawString(label, font, brush, rect, format)
            End Using
        End Sub

        Private Shared Function CenteredIconRect(buttonRect As RectangleF, icon As Image) As RectangleF
            Dim x = buttonRect.Left + (buttonRect.Width - icon.Width) / 2.0F
            Dim y = buttonRect.Top + (buttonRect.Height - icon.Height) / 2.0F
            Return New RectangleF(x, y, icon.Width, icon.Height)
        End Function

        Private Shared Function LoadBoardImage(fileName As String) As Image
            If String.IsNullOrWhiteSpace(fileName) Then Return Nothing

            SyncLock boardImages
                Dim cached As Image = Nothing
                If boardImages.TryGetValue(fileName, cached) Then Return cached

                For Each candidate In BoardImageCandidates(fileName)
                    If Not File.Exists(candidate) Then Continue For

                    Using loaded = Image.FromFile(candidate)
                        cached = New Bitmap(loaded)
                    End Using

                    boardImages(fileName) = cached
                    Return cached
                Next
            End SyncLock

            Return Nothing
        End Function

        Private Shared Iterator Function BoardImageCandidates(fileName As String) As IEnumerable(Of String)
            Yield Path.Combine(AppContext.BaseDirectory, fileName)
            Yield Path.Combine(Environment.CurrentDirectory, fileName)
            Yield Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\..\..\..\", fileName))
        End Function

        Private Shared Function RoundedPath(bounds As Rectangle, radius As Integer) As GraphicsPath
            Dim path As New GraphicsPath()
            Dim diameter = Math.Max(2, radius * 2)
            Dim rect = New Rectangle(bounds.Left, bounds.Top, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1))

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90)
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90)
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90)
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90)
            path.CloseFigure()
            Return path
        End Function

        Private Sub DrawBoardSilkscreen(g As Graphics)
            Dim shellRect = BoardToClient(New RectangleF(BoardPalmViewX, BoardPalmViewY, BoardLcdX - BoardPalmViewX, BoardPalmViewH))
            Using bg As New SolidBrush(SilkscreenFillColor(isDisplayAsleep))
                g.FillRectangle(bg, shellRect)
            End Using

            Dim silkscreen = LoadBoardImage("Silkscreen.png")
            If silkscreen IsNot Nothing Then
                Dim logicalShellWidth = BoardLcdX - BoardPalmViewX
                Dim logicalX = BoardPalmViewX + (logicalShellWidth - silkscreen.Width) / 2.0F
                Dim logicalY = BoardPalmViewY + (BoardPalmViewH - silkscreen.Height) / 2.0F
                g.DrawImage(silkscreen, BoardToClient(New RectangleF(logicalX, logicalY, silkscreen.Width, silkscreen.Height)))
                Return
            End If

            Using pen As New Pen(SilkscreenLineColor(isDisplayAsleep), 1.0F)
                g.DrawLine(pen, shellRect.Right, shellRect.Top, shellRect.Right, shellRect.Bottom)
                DrawPalmXLine(g, pen, 27)
                DrawPalmXLine(g, pen, 80)
                DrawPalmXLine(g, pen, 133)
            End Using

            Using brush As New SolidBrush(SilkscreenLineColor(isDisplayAsleep))
                DrawBoardPalmCircle(g, brush, 15, PalmConfig.LcdHeight + 17, 9)
                DrawBoardPalmCircle(g, brush, 15, PalmConfig.LcdHeight + 43, 9)
                DrawBoardPalmCircle(g, brush, 145, PalmConfig.LcdHeight + 17, 9)
                DrawBoardPalmCircle(g, brush, 145, PalmConfig.LcdHeight + 43, 9)
            End Using

            Dim abc = PalmDigitizerToBoard(52, PalmConfig.LcdHeight + 42)
            Dim nums = PalmDigitizerToBoard(105, PalmConfig.LcdHeight + 42)
            Using font As New Font(FontFamily.GenericSansSerif, Math.Max(7.0F, shellRect.Height * 0.045F), FontStyle.Bold),
                  brush As New SolidBrush(SilkscreenLineColor(isDisplayAsleep)),
                  format As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                g.DrawString("abc", font, brush, New RectangleF(abc.X - 28, abc.Y - 10, 56, 20), format)
                g.DrawString("123", font, brush, New RectangleF(nums.X - 28, nums.Y - 10, 56, 20), format)
            End Using
        End Sub

        Private Sub DrawPalmXLine(g As Graphics, pen As Pen, palmX As Integer)
            Dim a = PalmDigitizerToBoard(palmX, PalmConfig.LcdHeight + 4)
            Dim b = PalmDigitizerToBoard(palmX, PalmConfig.DigitizerHeight - 4)
            g.DrawLine(pen, a, b)
        End Sub

        Private Function PalmDigitizerToBoard(palmX As Integer, palmY As Integer) As PointF
            Dim bx = BoardPalmViewX + ((PalmConfig.DigitizerHeight - 1 - palmY) * BoardPalmViewW) / CSng(PalmConfig.DigitizerHeight)
            Dim by = BoardPalmViewY + (palmX * BoardPalmViewH) / CSng(PalmConfig.DigitizerWidth)
            Return BoardToClientPoint(bx, by)
        End Function

        Private Sub DrawBoardPalmCircle(g As Graphics, brush As Brush, palmX As Integer, palmY As Integer, radius As Integer)
            Dim center = PalmDigitizerToBoard(palmX, palmY)
            Dim boardRect = BoardDisplayRectangle()
            Dim scale = boardRect.Width / BoardWidth
            Dim r = radius * scale
            g.FillEllipse(brush, center.X - r, center.Y - r, r * 2, r * 2)
        End Sub

        Private Sub DrawBoardLcd(g As Graphics)
            Dim lcdRect = BoardToClient(New RectangleF(BoardLcdX, BoardLcdY, BoardLcdW, BoardLcdH))
            Using bg As New SolidBrush(If(isDisplayAsleep, SleepLcdBackgroundColor(), LcdBackgroundColor()))
                g.FillRectangle(bg, lcdRect)
            End Using

            If isDisplayAsleep OrElse frameBytes Is Nothing OrElse frameWidth <= 0 OrElse frameHeight <= 0 OrElse framePitch <= 0 Then Return

            Dim drawWidth = Math.Min(PalmConfig.LcdWidth, frameWidth)
            Dim drawHeight = Math.Min(PalmConfig.LcdHeight, frameHeight)
            Using bitmap = BuildFrameBitmap(drawWidth, drawHeight)
                Using displayBitmap = BuildIntegerScaledBitmap(bitmap, BoardLcdH, BoardLcdW)
                    Dim state = g.Save()
                    Try
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear
                        g.PixelOffsetMode = PixelOffsetMode.Half
                        g.TranslateTransform(BoardLcdX + BoardLcdW, BoardLcdY)
                        g.RotateTransform(90.0F)
                        g.DrawImage(displayBitmap, New RectangleF(0, 0, BoardLcdH, BoardLcdW))
                    Finally
                        g.Restore(state)
                    End Try
                End Using
            End Using
        End Sub

        Private Shared Function BuildIntegerScaledBitmap(source As Bitmap, targetWidth As Integer, targetHeight As Integer) As Bitmap
            Dim scaleX = targetWidth / CDbl(Math.Max(1, source.Width))
            Dim scaleY = targetHeight / CDbl(Math.Max(1, source.Height))
            Dim integerScale = Math.Max(1, CInt(Math.Round(Math.Max(scaleX, scaleY))))
            Dim width = source.Width * integerScale
            Dim height = source.Height * integerScale
            Dim scaled As New Bitmap(width, height, PixelFormat.Format32bppArgb)

            Using g = Graphics.FromImage(scaled)
                g.InterpolationMode = InterpolationMode.NearestNeighbor
                g.PixelOffsetMode = PixelOffsetMode.Half
                g.DrawImage(source, New Rectangle(0, 0, width, height))
            End Using

            Return scaled
        End Function

        Private Function BuildFrameBitmap(drawWidth As Integer, drawHeight As Integer) As Bitmap
            Dim bitmap As New Bitmap(drawWidth, drawHeight, PixelFormat.Format32bppArgb)
            Dim bounds As New Rectangle(0, 0, drawWidth, drawHeight)
            Dim data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            Dim strideInts = Math.Abs(data.Stride) \ 4
            Dim argb(strideInts * drawHeight - 1) As Integer
            Dim maxValue = Math.Max(1, (1 << Math.Min(frameBpp, 8)) - 1)
            Dim colors(maxValue) As Integer

            Try
                Dim background = If(UseFramePalette(), PixelColor(0, maxValue).ToArgb(), LcdBackgroundColor().ToArgb())
                For i = 0 To argb.Length - 1
                    argb(i) = background
                Next

                Dim firstColor = If(UseFramePalette(), 0, 1)
                For i = firstColor To maxValue
                    colors(i) = PixelColor(i, maxValue).ToArgb()
                Next

                For y = 0 To drawHeight - 1
                    Dim row = y * framePitch
                    Dim targetRow = y * strideInts
                    For x = 0 To drawWidth - 1
                        Dim value = GetPixelValue(row, x + framePan)
                        If value > 0 OrElse UseFramePalette() Then
                            argb(targetRow + x) = colors(value)
                        End If
                    Next
                Next

                Marshal.Copy(argb, 0, data.Scan0, argb.Length)
            Finally
                bitmap.UnlockBits(data)
            End Try

            Return bitmap
        End Function

        Private Function PageBackgroundColor() As Color
            If isDisplayAsleep Then Return ProfileColor(PalmConfig.SleepPageBackgroundR, PalmConfig.SleepPageBackgroundG, PalmConfig.SleepPageBackgroundB)

            Select Case displayMode
                Case DisplayRenderMode.InvertedGreenBacklight
                    Return ProfileColor(PalmConfig.BacklightPageBackgroundR, PalmConfig.BacklightPageBackgroundG, PalmConfig.BacklightPageBackgroundB)
                Case Else
                    Return Color.White
            End Select
        End Function

        Private Function LcdBackgroundColor() As Color
            Select Case displayMode
                Case DisplayRenderMode.InvertedGreenBacklight
                    Return ProfileColor(PalmConfig.BacklightLcdBackgroundR, PalmConfig.BacklightLcdBackgroundG, PalmConfig.BacklightLcdBackgroundB)
                Case Else
                    Return ProfileColor(PalmConfig.NormalLcdBackgroundR, PalmConfig.NormalLcdBackgroundG, PalmConfig.NormalLcdBackgroundB)
            End Select
        End Function

        Private Function SleepLcdBackgroundColor() As Color
            Select Case displayMode
                Case DisplayRenderMode.InvertedGreenBacklight
                    Return ProfileColor(PalmConfig.BacklightSleepLcdBackgroundR, PalmConfig.BacklightSleepLcdBackgroundG, PalmConfig.BacklightSleepLcdBackgroundB)
                Case Else
                    Return ProfileColor(PalmConfig.SleepLcdBackgroundR, PalmConfig.SleepLcdBackgroundG, PalmConfig.SleepLcdBackgroundB)
            End Select
        End Function

        Private Function PixelColor(value As Integer, maxValue As Integer) As Color
            If UseFramePalette() AndAlso value >= 0 AndAlso value < framePalette.Length Then
                Dim argb = framePalette(value)
                Dim scale = Math.Max(0.15, ContrastLevel())
                Return Color.FromArgb(255,
                                      CInt(((argb >> 16) And &HFFUI) * scale),
                                      CInt(((argb >> 8) And &HFFUI) * scale),
                                      CInt((argb And &HFFUI) * scale))
            End If

            Dim level = Math.Max(0.0, Math.Min(1.0, value / CDbl(Math.Max(1, maxValue))))

            Select Case displayMode
                Case DisplayRenderMode.InvertedGreenBacklight
                    Return Blend(
                        ProfileColor(PalmConfig.BacklightPixelLowR, PalmConfig.BacklightPixelLowG, PalmConfig.BacklightPixelLowB),
                        ProfileColor(PalmConfig.BacklightPixelHighR, PalmConfig.BacklightPixelHighG, PalmConfig.BacklightPixelHighB),
                        level * BacklightInkBlend())
                Case Else
                    Dim shade = CInt(Math.Round(255.0 - level * MonoLcdInkLevel()))
                    shade = Math.Max(0, Math.Min(255, shade))
                    Return Color.FromArgb(shade, shade, shade)
            End Select
        End Function

        Private Function UseFramePalette() As Boolean
            Return framePalette IsNot Nothing AndAlso framePalette.Length > 0 AndAlso displayMode = DisplayRenderMode.NormalMono
        End Function

        Private Function MonoLcdInkLevel() As Double
            Dim normalized = ContrastLevel()
            Dim adjusted = Math.Min(1.0, normalized / 0.72)
            Return 80.0 + 175.0 * Math.Pow(adjusted, 1.1)
        End Function

        Private Function BacklightInkBlend() As Double
            Dim normalized = ContrastLevel()
            Return Math.Max(0.04, Math.Pow(normalized, 1.35))
        End Function

        Private Function ContrastLevel() As Double
            Return ContrastRegisterByte(frameContrast) / 255.0
        End Function

        Private Shared Function ContrastRegisterByte(registerValue As UShort) As Integer
            If registerValue = 0US Then Return 255

            Dim lowByte = registerValue And &HFF
            If lowByte <> 0 Then Return lowByte

            Return (registerValue >> 8) And &HFF
        End Function

        Private Shared Function Blend(a As Color, b As Color, amount As Double) As Color
            Dim t = Math.Max(0.0, Math.Min(1.0, amount))
            Dim r = CInt(Math.Round(CInt(a.R) + (CInt(b.R) - CInt(a.R)) * t))
            Dim g = CInt(Math.Round(CInt(a.G) + (CInt(b.G) - CInt(a.G)) * t))
            Dim blue = CInt(Math.Round(CInt(a.B) + (CInt(b.B) - CInt(a.B)) * t))
            Return Color.FromArgb(r, g, blue)
        End Function

        Private Shared Function ProfileColor(r As Integer, g As Integer, b As Integer) As Color
            Return Color.FromArgb(r, g, b)
        End Function

        Private Sub DrawSilkscreen(g As Graphics, scaleX As Single, scaleY As Single, top As Single, asleep As Boolean)
            Dim h = PalmConfig.SilkscreenHeight * scaleY
            Dim lineColor = SilkscreenLineColor(asleep)
            Dim fillColor = SilkscreenFillColor(asleep)

            Using bg As New SolidBrush(fillColor)
                g.FillRectangle(bg, 0, top, PalmConfig.DigitizerWidth * scaleX, h)
            End Using

            Using pen As New Pen(lineColor, 1)
                g.DrawLine(pen, 0, top, PalmConfig.DigitizerWidth * scaleX, top)
                g.DrawLine(pen, 27 * scaleX, top + 4 * scaleY, 27 * scaleX, top + h - 4 * scaleY)
                g.DrawLine(pen, 133 * scaleX, top + 4 * scaleY, 133 * scaleX, top + h - 4 * scaleY)
                g.DrawLine(pen, 80 * scaleX, top + 4 * scaleY, 80 * scaleX, top + h - 4 * scaleY)
            End Using

            Using brush As New SolidBrush(lineColor)
                g.FillEllipse(brush, 6 * scaleX, top + 8 * scaleY, 18 * scaleX, 18 * scaleY)
                g.FillEllipse(brush, 6 * scaleX, top + 34 * scaleY, 18 * scaleX, 18 * scaleY)
                g.FillEllipse(brush, 136 * scaleX, top + 8 * scaleY, 18 * scaleX, 18 * scaleY)
                g.FillEllipse(brush, 136 * scaleX, top + 34 * scaleY, 18 * scaleX, 18 * scaleY)
            End Using

            Using font As New Font(FontFamily.GenericSansSerif, Math.Max(7.0F, 8.0F * scaleY), FontStyle.Bold),
                  brush As New SolidBrush(lineColor)
                g.DrawString("abc", font, brush, 42 * scaleX, top + 38 * scaleY)
                g.DrawString("123", font, brush, 92 * scaleX, top + 38 * scaleY)
            End Using
        End Sub

        Private Function SilkscreenLineColor(asleep As Boolean) As Color
            If asleep Then Return ProfileColor(PalmConfig.SleepLineR, PalmConfig.SleepLineG, PalmConfig.SleepLineB)
            If displayMode = DisplayRenderMode.InvertedGreenBacklight Then Return ProfileColor(PalmConfig.BacklightLineR, PalmConfig.BacklightLineG, PalmConfig.BacklightLineB)
            Return Color.Black
        End Function

        Private Function SilkscreenFillColor(asleep As Boolean) As Color
            If asleep Then Return ProfileColor(PalmConfig.SleepSilkscreenFillR, PalmConfig.SleepSilkscreenFillG, PalmConfig.SleepSilkscreenFillB)

            Select Case displayMode
                Case DisplayRenderMode.InvertedGreenBacklight
                    Return ProfileColor(PalmConfig.BacklightSilkscreenFillR, PalmConfig.BacklightSilkscreenFillG, PalmConfig.BacklightSilkscreenFillB)
                Case Else
                    Return ProfileColor(PalmConfig.NormalSilkscreenFillR, PalmConfig.NormalSilkscreenFillG, PalmConfig.NormalSilkscreenFillB)
            End Select
        End Function

        Private Function GetPixelValue(row As Integer, x As Integer) As Integer
            If frameBpp <> 1 AndAlso frameBpp <> 2 AndAlso frameBpp <> 4 AndAlso frameBpp <> 8 Then Return 0

            Dim bitIndex = x * frameBpp
            Dim byteIndex = row + (bitIndex \ 8)
            If byteIndex < 0 OrElse byteIndex >= frameBytes.Length Then Return 0

            Dim shift = 8 - frameBpp - (bitIndex And 7)
            If shift < 0 Then Return 0

            Dim mask = (1 << frameBpp) - 1
            Return (frameBytes(byteIndex) >> shift) And mask
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If layoutModeValue = DisplayLayoutMode.Esp32Board Then
                HandleBoardMouseDown(e)
                Return
            End If

            Capture = True
            RaisePenEvent(True, e.Location)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If layoutModeValue = DisplayLayoutMode.Esp32Board Then
                If Capture AndAlso e.Button = MouseButtons.Left AndAlso boardPenActive Then
                    RaiseBoardPenEvent(True, e.Location)
                End If
                Return
            End If

            If Capture AndAlso e.Button = MouseButtons.Left Then
                RaisePenEvent(True, e.Location)
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If layoutModeValue = DisplayLayoutMode.Esp32Board Then
                If activeBoardButtonBits <> 0US Then
                    ReleaseBoardButton()
                ElseIf boardPenActive Then
                    RaiseBoardPenEvent(False, e.Location)
                    boardPenActive = False
                End If
                Capture = False
                Return
            End If

            RaisePenEvent(False, e.Location)
            Capture = False
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            If layoutModeValue = DisplayLayoutMode.Esp32Board Then
                If Capture Then
                    If activeBoardButtonBits <> 0US Then
                        ReleaseBoardButton()
                    ElseIf boardPenActive Then
                        RaiseBoardPenEvent(False, lastPoint)
                        boardPenActive = False
                    End If
                    Capture = False
                End If
                Return
            End If

            If Capture Then
                RaisePenEvent(False, lastPoint)
                Capture = False
            End If
        End Sub

        Private Sub RaisePenEvent(down As Boolean, point As Point)
            lastPoint = point
            Dim x = CInt(Math.Floor(point.X * PalmConfig.DigitizerWidth / CDbl(Math.Max(1, ClientSize.Width))))
            Dim y = CInt(Math.Floor(point.Y * PalmConfig.DigitizerHeight / CDbl(Math.Max(1, ClientSize.Height))))
            x = Math.Max(0, Math.Min(PalmConfig.DigitizerWidth - 1, x))
            y = Math.Max(0, Math.Min(PalmConfig.DigitizerHeight - 1, y))
            RaiseEvent PenChanged(down, x, y)
        End Sub

        Private Sub HandleBoardMouseDown(e As MouseEventArgs)
            If e.Button <> MouseButtons.Left Then Return

            Dim boardX As Single
            Dim boardY As Single
            If Not TryClientToBoard(e.Location, boardX, boardY) Then Return

            Dim label As String = ""
            Dim bits = BoardButtonBitsAt(boardX, boardY, label)
            Capture = True
            If bits <> 0US Then
                activeBoardButtonBits = bits
                activeBoardButtonLabel = label
                Invalidate()
                RaiseEvent ButtonChanged(bits, True, label)
                Return
            End If

            If BoardPalmSurfaceContains(boardX, boardY) Then
                boardPenActive = True
                RaiseBoardPenEvent(True, e.Location)
            End If
        End Sub

        Private Sub ReleaseBoardButton()
            If activeBoardButtonBits = 0US Then Return

            Dim bits = activeBoardButtonBits
            Dim label = activeBoardButtonLabel
            activeBoardButtonBits = 0US
            activeBoardButtonLabel = ""
            Invalidate()
            RaiseEvent ButtonChanged(bits, False, label)
        End Sub

        Private Function BoardButtonBitsAt(boardX As Single, boardY As Single, ByRef label As String) As UShort
            label = ""
            If boardY < 0 OrElse boardY >= BoardHeight Then Return 0US

            If boardX >= 0 AndAlso boardX < BoardLeftButtonX1 Then
                Dim index = CInt(Math.Floor(boardY * BoardButtonCount / CDbl(BoardHeight)))
                index = Math.Max(0, Math.Min(BoardButtonCount - 1, index))
                label = BoardButtonLabelForLeftIndex(index)
                Return BoardButtonBitsForLeftIndex(index)
            End If

            If boardX >= BoardRightButtonX0 AndAlso boardX < BoardWidth Then
                Dim index = CInt(Math.Floor(boardY * BoardButtonCount / CDbl(BoardHeight)))
                index = Math.Max(0, Math.Min(BoardButtonCount - 1, index))
                If index <= 3 Then
                    label = "Power"
                    Return KeyBitPower
                End If
                If index = 4 Then
                    label = "Save State"
                    Return KeyBitSaveState
                End If
                label = "Reset"
                Return KeyBitResetOs
            End If

            Return 0US
        End Function

        Private Shared Function BoardButtonBitsForLeftIndex(index As Integer) As UShort
            Select Case index
                Case 0
                    Return KeyBitHard1
                Case 1
                    Return KeyBitHard2
                Case 2
                    Return KeyBitPageUp
                Case 3
                    Return KeyBitPageDown
                Case 4
                    Return KeyBitHard3
                Case 5
                    Return KeyBitHard4
                Case Else
                    Return 0US
            End Select
        End Function

        Private Shared Function BoardButtonLabelForLeftIndex(index As Integer) As String
            Select Case index
                Case 0
                    Return "App 1"
                Case 1
                    Return "App 2"
                Case 2
                    Return "Up"
                Case 3
                    Return "Down"
                Case 4
                    Return "App 3"
                Case 5
                    Return "App 4"
                Case Else
                    Return ""
            End Select
        End Function

        Private Shared Function BoardButtonIconForLeftIndex(index As Integer) As String
            Select Case index
                Case 2
                    Return "Btn_Up.png"
                Case 3
                    Return "Btn_Down.png"
                Case 0, 1, 4, 5
                    Return "Btn_App.png"
                Case Else
                    Return ""
            End Select
        End Function

        Private Shared Function BoardPalmSurfaceContains(boardX As Single, boardY As Single) As Boolean
            Return boardX >= BoardPalmViewX AndAlso boardX < BoardPalmViewX + BoardPalmViewW AndAlso
                   boardY >= BoardPalmViewY AndAlso boardY < BoardPalmViewY + BoardPalmViewH
        End Function

        Private Sub RaiseBoardPenEvent(down As Boolean, point As Point)
            lastPoint = point

            Dim boardX As Single
            Dim boardY As Single
            If Not TryClientToBoard(point, boardX, boardY) Then
                RaiseEvent PenChanged(False, 0, 0)
                Return
            End If

            Dim rotX = (boardX - BoardPalmViewX) * PalmConfig.DigitizerHeight / CSng(BoardPalmViewW)
            Dim rotY = (boardY - BoardPalmViewY) * PalmConfig.DigitizerWidth / CSng(BoardPalmViewH)
            Dim x = CInt(Math.Floor(rotY))
            Dim y = CInt(Math.Floor(PalmConfig.DigitizerHeight - 1 - rotX))
            x = Math.Max(0, Math.Min(PalmConfig.DigitizerWidth - 1, x))
            y = Math.Max(0, Math.Min(PalmConfig.DigitizerHeight - 1, y))
            RaiseEvent PenChanged(down, x, y)
        End Sub
    End Class
End Namespace
