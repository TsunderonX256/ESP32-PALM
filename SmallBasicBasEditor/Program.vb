Imports System
Imports System.Windows.Forms

Namespace SmallBasicBasEditor
    Friend Module Program
        <STAThread>
        Sub Main(args As String())
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            Dim form As New MainForm()
            If args.Length > 0 AndAlso IO.File.Exists(args(0)) Then
                form.OpenFile(args(0))
            End If

            Application.Run(form)
        End Sub
    End Module
End Namespace
