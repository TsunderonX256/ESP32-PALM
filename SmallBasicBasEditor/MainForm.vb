Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports Microsoft.VisualBasic

Namespace SmallBasicBasEditor
    Friend NotInheritable Class MainForm
        Inherits Form

        Private ReadOnly editor As TextBox
        Private ReadOnly sectionList As ListBox
        Private ReadOnly status As ToolStripStatusLabel
        Private ReadOnly sections As New List(Of SmallBasicBasSection)()
        Private currentPath As String = String.Empty
        Private dirty As Boolean
        Private currentSectionIndex As Integer = -1
        Private loadingEditor As Boolean

        Public Sub New()
            Text = "SmallBASIC .bas Editor"
            StartPosition = FormStartPosition.CenterScreen
            MinimumSize = New Size(720, 480)
            Size = New Size(980, 720)

            Dim menu As New MenuStrip()
            Dim fileMenu As New ToolStripMenuItem("&File")
            Dim newItem = MenuItem("&New", Keys.Control Or Keys.N, AddressOf NewFile)
            Dim openItem = MenuItem("&Open...", Keys.Control Or Keys.O, AddressOf OpenFromDialog)
            Dim saveItem = MenuItem("&Save", Keys.Control Or Keys.S, AddressOf SaveCurrent)
            Dim saveAsItem = MenuItem("Save &As...", Keys.Control Or Keys.Shift Or Keys.S, AddressOf SaveAsFromDialog)
            Dim exportItem = MenuItem("Export Plain &Text...", Keys.Control Or Keys.E, AddressOf ExportPlainText)
            Dim exitItem = MenuItem("E&xit", Keys.Alt Or Keys.F4, Sub() Close())
            fileMenu.DropDownItems.AddRange(New ToolStripItem() {newItem, openItem, New ToolStripSeparator(), saveItem, saveAsItem, exportItem, New ToolStripSeparator(), exitItem})
            menu.Items.Add(fileMenu)

            Dim sectionMenu As New ToolStripMenuItem("&Section")
            Dim addSectionItem = MenuItem("&Add", Keys.Control Or Keys.Shift Or Keys.N, AddressOf AddSection)
            Dim renameSectionItem = MenuItem("&Rename", Keys.F2, AddressOf RenameSection)
            Dim deleteSectionItem = MenuItem("&Delete", Keys.Control Or Keys.Delete, AddressOf DeleteSection)
            sectionMenu.DropDownItems.AddRange(New ToolStripItem() {addSectionItem, renameSectionItem, deleteSectionItem})
            menu.Items.Add(sectionMenu)

            sectionList = New ListBox() With {
                .Dock = DockStyle.Fill,
                .IntegralHeight = False
            }
            AddHandler sectionList.SelectedIndexChanged, AddressOf SelectSection

            editor = New TextBox() With {
                .AcceptsReturn = True,
                .AcceptsTab = True,
                .Dock = DockStyle.Fill,
                .Font = New Font(FontFamily.GenericMonospace, 11.0F),
                .Multiline = True,
                .ScrollBars = ScrollBars.Both,
                .WordWrap = False
            }
            AddHandler editor.TextChanged, Sub()
                                               If loadingEditor OrElse currentSectionIndex < 0 Then Return
                                               sections(currentSectionIndex).Source = editor.Text
                                               dirty = True
                                               UpdateTitle()
                                           End Sub

            Dim split As New SplitContainer() With {
                .Dock = DockStyle.Fill,
                .FixedPanel = FixedPanel.Panel1,
                .SplitterDistance = 230
            }
            split.Panel1.Controls.Add(sectionList)
            split.Panel2.Controls.Add(editor)

            Dim statusStrip As New StatusStrip()
            status = New ToolStripStatusLabel("Ready")
            statusStrip.Items.Add(status)

            Controls.Add(split)
            Controls.Add(statusStrip)
            Controls.Add(menu)
            MainMenuStrip = menu

            AddHandler FormClosing, AddressOf ConfirmClose
            ResetSections(New List(Of SmallBasicBasSection) From {
                New SmallBasicBasSection("Section 1", String.Empty)
            }, 0)
        End Sub

        Public Sub OpenFile(path As String)
            If Not ConfirmDiscardChanges() Then Return

            ResetSections(SmallBasicBasDocument.ReadSections(path), 0)
            currentPath = path
            dirty = False
            status.Text = $"Opened {IO.Path.GetFileName(path)} ({sections.Count} section{If(sections.Count = 1, String.Empty, "s")})"
            UpdateTitle()
        End Sub

        Private Shared Function MenuItem(text As String, shortcut As Keys, handler As EventHandler) As ToolStripMenuItem
            Dim item As New ToolStripMenuItem(text)
            item.ShortcutKeys = shortcut
            AddHandler item.Click, handler
            Return item
        End Function

        Private Sub NewFile(sender As Object, e As EventArgs)
            If Not ConfirmDiscardChanges() Then Return

            ResetSections(New List(Of SmallBasicBasSection) From {
                New SmallBasicBasSection("Section 1", String.Empty)
            }, 0)
            currentPath = String.Empty
            dirty = False
            status.Text = "New file"
            UpdateTitle()
        End Sub

        Private Sub OpenFromDialog(sender As Object, e As EventArgs)
            Using dialog As New OpenFileDialog()
                dialog.Filter = "SmallBASIC files (*.bas)|*.bas|All files (*.*)|*.*"
                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    OpenFile(dialog.FileName)
                End If
            End Using
        End Sub

        Private Sub SaveCurrent(sender As Object, e As EventArgs)
            If String.IsNullOrEmpty(currentPath) Then
                SaveAsFromDialog(sender, e)
                Return
            End If

            SaveTo(currentPath)
        End Sub

        Private Sub SaveAsFromDialog(sender As Object, e As EventArgs)
            Using dialog As New SaveFileDialog()
                dialog.Filter = "SmallBASIC files (*.bas)|*.bas|All files (*.*)|*.*"
                dialog.DefaultExt = "bas"
                If Not String.IsNullOrEmpty(currentPath) Then
                    dialog.FileName = Path.GetFileName(currentPath)
                    dialog.InitialDirectory = Path.GetDirectoryName(currentPath)
                Else
                    dialog.FileName = "program.bas"
                End If

                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    SaveTo(dialog.FileName)
                End If
            End Using
        End Sub

        Private Sub ExportPlainText(sender As Object, e As EventArgs)
            CommitEditorToCurrentSection()

            Using dialog As New SaveFileDialog()
                dialog.Filter = "Text files (*.txt)|*.txt|BASIC source (*.bas)|*.bas|All files (*.*)|*.*"
                dialog.DefaultExt = "txt"
                dialog.FileName = If(String.IsNullOrEmpty(currentPath), "program.txt", Path.GetFileNameWithoutExtension(currentPath) & ".txt")

                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    File.WriteAllText(dialog.FileName, CombinedSource())
                    status.Text = $"Exported {Path.GetFileName(dialog.FileName)}"
                End If
            End Using
        End Sub

        Private Sub SaveTo(path As String)
            CommitEditorToCurrentSection()
            SmallBasicBasDocument.WriteSections(path, sections)
            currentPath = path
            dirty = False
            status.Text = $"Saved {IO.Path.GetFileName(path)}"
            UpdateTitle()
        End Sub

        Private Sub SelectSection(sender As Object, e As EventArgs)
            If sectionList.SelectedIndex = currentSectionIndex Then Return

            CommitEditorToCurrentSection()
            currentSectionIndex = sectionList.SelectedIndex
            LoadCurrentSection()
        End Sub

        Private Sub AddSection(sender As Object, e As EventArgs)
            CommitEditorToCurrentSection()

            Dim sectionName = $"Section {sections.Count + 1}"
            sections.Add(New SmallBasicBasSection(sectionName, String.Empty))
            RefreshSectionList(sections.Count - 1)
            dirty = True
            status.Text = $"Added {sectionName}"
            UpdateTitle()
        End Sub

        Private Sub RenameSection(sender As Object, e As EventArgs)
            If currentSectionIndex < 0 Then Return

            Dim oldName = sections(currentSectionIndex).Name
            Dim newName = Interaction.InputBox("Section name:", "Rename Section", oldName).Trim()
            If newName.Length = 0 OrElse newName = oldName Then Return

            sections(currentSectionIndex).Name = newName
            RefreshSectionList(currentSectionIndex)
            dirty = True
            status.Text = $"Renamed {oldName}"
            UpdateTitle()
        End Sub

        Private Sub DeleteSection(sender As Object, e As EventArgs)
            If currentSectionIndex < 0 OrElse sections.Count <= 1 Then Return

            Dim sectionName = sections(currentSectionIndex).Name
            Dim result = MessageBox.Show(Me, $"Delete {sectionName}?", "SmallBASIC .bas Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If result <> DialogResult.Yes Then Return

            Dim nextIndex = Math.Min(currentSectionIndex, sections.Count - 2)
            sections.RemoveAt(currentSectionIndex)
            RefreshSectionList(nextIndex)
            dirty = True
            status.Text = $"Deleted {sectionName}"
            UpdateTitle()
        End Sub

        Private Sub ConfirmClose(sender As Object, e As FormClosingEventArgs)
            If Not ConfirmDiscardChanges() Then
                e.Cancel = True
            End If
        End Sub

        Private Function ConfirmDiscardChanges() As Boolean
            If Not dirty Then Return True

            Dim result = MessageBox.Show(Me, "Discard unsaved changes?", "SmallBASIC .bas Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            Return result = DialogResult.Yes
        End Function

        Private Sub ResetSections(newSections As List(Of SmallBasicBasSection), selectedIndex As Integer)
            sections.Clear()
            sections.AddRange(newSections)
            If sections.Count = 0 Then sections.Add(New SmallBasicBasSection("Section 1", String.Empty))

            RefreshSectionList(Math.Max(0, Math.Min(selectedIndex, sections.Count - 1)))
        End Sub

        Private Sub RefreshSectionList(selectedIndex As Integer)
            sectionList.BeginUpdate()
            sectionList.Items.Clear()
            For Each section In sections
                sectionList.Items.Add(section)
            Next
            sectionList.EndUpdate()

            currentSectionIndex = -1
            sectionList.SelectedIndex = selectedIndex
        End Sub

        Private Sub CommitEditorToCurrentSection()
            If currentSectionIndex >= 0 AndAlso currentSectionIndex < sections.Count Then
                sections(currentSectionIndex).Source = editor.Text
            End If
        End Sub

        Private Sub LoadCurrentSection()
            loadingEditor = True
            Try
                If currentSectionIndex >= 0 AndAlso currentSectionIndex < sections.Count Then
                    editor.Text = sections(currentSectionIndex).Source
                    status.Text = sections(currentSectionIndex).Name
                Else
                    editor.Clear()
                End If
            Finally
                loadingEditor = False
            End Try
        End Sub

        Private Function CombinedSource() As String
            Dim values As New List(Of String)()
            For Each section In sections
                values.Add(section.Source)
            Next

            Return String.Join(Environment.NewLine & Environment.NewLine, values)
        End Function

        Private Sub UpdateTitle()
            Dim name = If(String.IsNullOrEmpty(currentPath), "Untitled", Path.GetFileName(currentPath))
            Text = $"{If(dirty, "*", String.Empty)}{name} - SmallBASIC .bas Editor"
        End Sub
    End Class
End Namespace
