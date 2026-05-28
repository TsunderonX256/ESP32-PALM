Imports System.Drawing
Imports System.Windows.Forms

Namespace PalmDesktopHarness
    Friend NotInheritable Class LcdPanel
        Inherits Control

        Private frameBytes As Byte()
        Private frameWidth As Integer
        Private frameHeight As Integer
        Private framePitch As Integer
        Private frameBpp As Integer = 1
        Private framePan As Integer
        Private lastPoint As Point
        Private isDisplayAsleep As Boolean

        Public Event PenChanged(down As Boolean, x As Integer, y As Integer)

        Public Sub New()
            DoubleBuffered = True
            BackColor = Color.White
            Size = New Size(PalmConfig.DigitizerWidth * 2, PalmConfig.DigitizerHeight * 2)
            MinimumSize = Size
        End Sub

        Public Sub UpdateFrame(bytes As Byte(), width As Integer, height As Integer, pitch As Integer, bpp As Integer, pan As Integer)
            frameBytes = bytes
            frameWidth = width
            frameHeight = height
            framePitch = pitch
            frameBpp = bpp
            framePan = pan
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

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)

            Dim scaleX = ClientSize.Width / CSng(PalmConfig.DigitizerWidth)
            Dim scaleY = ClientSize.Height / CSng(PalmConfig.DigitizerHeight)
            Dim lcdScaleY = scaleY
            Dim silkscreenTop = PalmConfig.LcdHeight * scaleY
            Dim lcdRect As New RectangleF(0, 0, PalmConfig.LcdWidth * scaleX, PalmConfig.LcdHeight * lcdScaleY)

            e.Graphics.Clear(If(isDisplayAsleep, Color.FromArgb(224, 228, 214), Color.White))
            e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor
            e.Graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half

            If isDisplayAsleep Then
                Using bg As New SolidBrush(Color.FromArgb(204, 211, 194))
                    e.Graphics.FillRectangle(bg, lcdRect)
                End Using
            ElseIf frameBytes IsNot Nothing AndAlso frameWidth > 0 AndAlso frameHeight > 0 AndAlso framePitch > 0 Then
                Dim drawWidth = Math.Min(PalmConfig.LcdWidth, frameWidth)
                Dim drawHeight = Math.Min(PalmConfig.LcdHeight, frameHeight)
                Dim maxValue = Math.Max(1, (1 << Math.Min(frameBpp, 8)) - 1)
                Dim brushes(maxValue) As SolidBrush

                Try
                    For i = 1 To maxValue
                        Dim shade = 255 - CInt(Math.Round(i * 255.0 / maxValue))
                        brushes(i) = New SolidBrush(Color.FromArgb(shade, shade, shade))
                    Next

                    For y = 0 To drawHeight - 1
                        Dim row = y * framePitch
                        For x = 0 To drawWidth - 1
                            Dim value = GetPixelValue(row, x + framePan)
                            If value > 0 Then
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

        Private Sub DrawSilkscreen(g As Graphics, scaleX As Single, scaleY As Single, top As Single, asleep As Boolean)
            Dim h = PalmConfig.SilkscreenHeight * scaleY
            Dim lineColor = If(asleep, Color.FromArgb(90, 95, 80), Color.Black)
            Dim fillColor = If(asleep, Color.FromArgb(134, 142, 116), Color.FromArgb(150, 160, 130))

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
            Capture = True
            RaisePenEvent(True, e.Location)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If Capture AndAlso e.Button = MouseButtons.Left Then
                RaisePenEvent(True, e.Location)
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            RaisePenEvent(False, e.Location)
            Capture = False
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
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
    End Class
End Namespace
