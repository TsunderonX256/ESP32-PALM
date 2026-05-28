Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Namespace PalmDesktopHarness
    Friend NotInheritable Class HardwareButtonPanel
        Inherits Control

        Private Structure ButtonSpec
            Public ReadOnly Label As String
            Public ReadOnly Bits As UShort
            Public ReadOnly Bounds As Rectangle
            Public ReadOnly Ellipse As Boolean

            Public Sub New(label As String, bits As UShort, bounds As Rectangle, ellipse As Boolean)
                Me.Label = label
                Me.Bits = bits
                Me.Bounds = bounds
                Me.Ellipse = ellipse
            End Sub
        End Structure

        Private ReadOnly buttons As ButtonSpec()
        Private activeIndex As Integer = -1

        Public Event ButtonChanged(bits As UShort, down As Boolean, label As String)

        Public Sub New()
            DoubleBuffered = True
            Size = New Size(PalmConfig.DigitizerWidth * 2, 78)
            MinimumSize = Size
            BackColor = SystemColors.Control

            buttons = {
                New ButtonSpec("App 1", &H8US, New Rectangle(14, 18, 54, 42), False),
                New ButtonSpec("App 2", &H10US, New Rectangle(82, 18, 54, 42), False),
                New ButtonSpec("Up", &H2US, New Rectangle(148, 8, 30, 30), False),
                New ButtonSpec("Down", &H4US, New Rectangle(148, 48, 30, 30), False),
                New ButtonSpec("App 3", &H20US, New Rectangle(190, 18, 54, 42), False),
                New ButtonSpec("App 4", &H40US, New Rectangle(254, 18, 54, 42), False)
            }
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)

            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            e.Graphics.Clear(BackColor)

            For i = 0 To buttons.Length - 1
                DrawButton(e.Graphics, buttons(i), i = activeIndex)
            Next
        End Sub

        Private Sub DrawButton(g As Graphics, spec As ButtonSpec, down As Boolean)
            Dim outline = If(down, SystemColors.Highlight, SystemColors.ControlDarkDark)

            Using path = RoundedPath(spec.Bounds, 8)
                If down Then
                    Using brush As New SolidBrush(Color.FromArgb(80, SystemColors.Highlight))
                        g.FillPath(brush, path)
                    End Using
                End If

                Using pen As New Pen(outline, If(down, 2.0F, 1.0F))
                    g.DrawPath(pen, path)
                End Using
            End Using
        End Sub

        Private Shared Function RoundedPath(bounds As Rectangle, radius As Integer) As GraphicsPath
            Dim path As New GraphicsPath()
            Dim diameter = Math.Max(2, radius * 2)
            Dim rect = New Rectangle(bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1)

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90)
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90)
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90)
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90)
            path.CloseFigure()
            Return path
        End Function

        Private Shared Sub DrawCenteredText(g As Graphics, text As String, font As Font, brush As Brush, bounds As Rectangle)
            Using format As New StringFormat With {
                .Alignment = StringAlignment.Center,
                .LineAlignment = StringAlignment.Center
            }
                g.DrawString(text, font, brush, bounds, format)
            End Using
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button <> MouseButtons.Left Then Return

            activeIndex = HitTest(e.Location)
            If activeIndex < 0 Then Return

            Capture = True
            Invalidate()
            RaiseEvent ButtonChanged(buttons(activeIndex).Bits, True, buttons(activeIndex).Label)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            ReleaseActiveButton()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            If Capture Then ReleaseActiveButton()
        End Sub

        Private Sub ReleaseActiveButton()
            If activeIndex < 0 Then
                Capture = False
                Return
            End If

            Dim spec = buttons(activeIndex)
            activeIndex = -1
            Capture = False
            Invalidate()
            RaiseEvent ButtonChanged(spec.Bits, False, spec.Label)
        End Sub

        Private Function HitTest(point As Point) As Integer
            For i = 0 To buttons.Length - 1
                Dim spec = buttons(i)
                If Not spec.Bounds.Contains(point) Then Continue For

                If spec.Ellipse Then
                    Dim rx = spec.Bounds.Width / 2.0
                    Dim ry = spec.Bounds.Height / 2.0
                    Dim cx = spec.Bounds.Left + rx
                    Dim cy = spec.Bounds.Top + ry
                    Dim dx = (point.X - cx) / rx
                    Dim dy = (point.Y - cy) / ry
                    If dx * dx + dy * dy > 1.0 Then Continue For
                End If

                Return i
            Next

            Return -1
        End Function
    End Class
End Namespace
