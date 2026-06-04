Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.IO.Ports
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Namespace PalmBeamCom
    Friend NotInheritable Class MainForm
        Inherits Form

        Private ReadOnly portCombo As ComboBox
        Private ReadOnly baudCombo As ComboBox
        Private ReadOnly connectButton As Button
        Private ReadOnly refreshButton As Button
        Private ReadOnly beamButton As Button
        Private ReadOnly captureBox As TextBox
        Private ReadOnly browseButton As Button
        Private ReadOnly traceBox As TextBox
        Private ReadOnly statusLabel As Label
        Private ReadOnly serviceTimer As Timer
        Private ReadOnly serialLock As New Object()
        Private ReadOnly rxBytes As New List(Of Byte)
        Private serial As SerialPort

        Private irdaFrameBuffer As New List(Of Byte)
        Private irdaInFrame As Boolean
        Private irdaEscaped As Boolean
        Private irdaRespondedInDiscovery As Boolean
        Private irdaLastDiscoverySource As UInteger
        Private irdaConnectionAddress As Byte
        Private irdaReceiveNext As Byte
        Private irdaTransmitNext As Byte
        Private irdaLastInformationResponse As Byte()
        Private irdaLastInformationResponseLabel As String = ""
        Private irdaObexObjectName As String = ""
        Private irdaObexObjectType As String = ""
        Private ReadOnly irdaObexBody As New List(Of Byte)
        Private ReadOnly irdaObexPacket As New List(Of Byte)
        Private irdaObexExpectedLength As Integer
        Private irdaObexClientLsap As Byte
        Private pendingIrdaResponse As Byte()
        Private pendingIrdaResponseLabel As String = ""
        Private pendingIrdaResponseDueTick As Long
        Private ReadOnly pendingIrdaResponses As New Queue(Of IrdaPendingResponse)
        Private irdaTxQuietSinceTick As Long

        Private irdaBeamState As IrdaBeamSendState = IrdaBeamSendState.Idle
        Private irdaBeamPeerAddress As UInteger
        Private irdaBeamConnectionAddress As Byte
        Private irdaBeamReceiveNext As Byte
        Private irdaBeamTransmitNext As Byte
        Private irdaBeamFileName As String = ""
        Private irdaBeamFileBytes As Byte()
        Private irdaBeamOffset As Integer
        Private irdaBeamLastProgressTick As Long
        Private irdaBeamDiscoverySlot As Integer
        Private irdaBeamEmptyEndBodySent As Boolean

        Private Const IrdaSirBof As Byte = &HC0
        Private Const IrdaSirEof As Byte = &HC1
        Private Const IrdaSirEscape As Byte = &H7D
        Private Const IrdaBroadcastAddress As Byte = &HFF
        Private Const IrdaUiControl As Byte = &H3F
        Private Const IrdaDiscoveryResponseAddress As Byte = &HFE
        Private Const IrdaXidResponseControl As Byte = &HBF
        Private Const IrdaXidFormat As Byte = &H1
        Private Const IrdaSnrmControl As Byte = &H93
        Private Const IrdaUaResponseControl As Byte = &H73
        Private Const IrdaDiscControl As Byte = &H53
        Private Const IrdaRrPollFinalControl As Byte = &H11
        Private Const IrdaFinalBit As Byte = &H10
        Private Const IrdaControlBit As Byte = &H80
        Private Const IrdaLmpConnectCommand As Byte = &H1
        Private Const IrdaLmpConnectConfirm As Byte = &H81
        Private Const IrdaIasGetValueByClass As Byte = &H4
        Private Const IrdaIasLast As Byte = &H80
        Private Const IrdaIasSuccess As Byte = &H0
        Private Const IrdaIasInteger As Byte = &H1
        Private Const IrdaObexLsap As Byte = &H2
        Private Const IrdaHostAddress As UInteger = &H45535032UI
        Private Const IrdaTurnaroundDelayMs As Long = 20
        Private Const IrdaConnectedTurnaroundDelayMs As Long = 0
        Private Const IrdaTxQuietBeforeResponseMs As Long = 2
        Private Const IrdaBeamBodyChunkSize As Integer = 40
        Private Const IrdaBeamDiscoveryRetryMs As Long = 250
        Private Const IrdaBeamTimeoutMs As Long = 15000
        Private Const PalmMemoMaxBytes As Integer = 4096

        Private NotInheritable Class IrdaPendingResponse
            Public Sub New(frameBytes As Byte(), labelText As String, dueTick As Long)
                Frame = frameBytes
                Label = labelText
                Due = dueTick
            End Sub

            Public ReadOnly Frame As Byte()
            Public ReadOnly Label As String
            Public ReadOnly Due As Long
        End Class

        Private Enum IrdaBeamSendState
            Idle
            Discover
            Snrm
            IasConnect
            ObexLmpConnect
            ObexConnect
            ObexPutMeta
            ObexPutBody
            ObexDisconnect
            LinkDisconnect
        End Enum

        Public Sub New()
            Text = "Palm Beam COM"
            StartPosition = FormStartPosition.CenterScreen
            Size = New Size(760, 560)
            MinimumSize = New Size(640, 460)

            Dim root As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 3,
                .Padding = New Padding(12)
            }
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Controls.Add(root)

            Dim portRow As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
            portRow.Controls.Add(New Label With {.Text = "Port", .AutoSize = True, .Margin = New Padding(0, 7, 4, 0)})
            portCombo = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120}
            portRow.Controls.Add(portCombo)
            portRow.Controls.Add(New Label With {.Text = "Baud", .AutoSize = True, .Margin = New Padding(12, 7, 4, 0)})
            baudCombo = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 100}
            baudCombo.Items.AddRange(New Object() {"9600", "19200", "38400", "57600", "115200"})
            baudCombo.SelectedItem = "9600"
            portRow.Controls.Add(baudCombo)
            refreshButton = New Button With {.Text = "Refresh", .Width = 80}
            AddHandler refreshButton.Click, Sub() RefreshPorts()
            portRow.Controls.Add(refreshButton)
            connectButton = New Button With {.Text = "Connect", .Width = 90}
            AddHandler connectButton.Click, AddressOf ConnectButton_Click
            portRow.Controls.Add(connectButton)
            beamButton = New Button With {.Text = "Beam File...", .Width = 100, .Enabled = False}
            AddHandler beamButton.Click, AddressOf BeamButton_Click
            portRow.Controls.Add(beamButton)
            statusLabel = New Label With {.Text = "Disconnected", .AutoSize = True, .Margin = New Padding(12, 7, 0, 0)}
            portRow.Controls.Add(statusLabel)
            root.Controls.Add(portRow, 0, 0)

            Dim captureRow As New TableLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .ColumnCount = 3}
            captureRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            captureRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            captureRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            captureRow.Controls.Add(New Label With {.Text = "Capture folder", .AutoSize = True, .Margin = New Padding(0, 7, 8, 0)}, 0, 0)
            captureBox = New TextBox With {.Dock = DockStyle.Fill, .Text = Path.Combine(AppContext.BaseDirectory, "BeamCapture")}
            captureRow.Controls.Add(captureBox, 1, 0)
            browseButton = New Button With {.Text = "Browse...", .Width = 90}
            AddHandler browseButton.Click, AddressOf BrowseButton_Click
            captureRow.Controls.Add(browseButton, 2, 0)
            root.Controls.Add(captureRow, 0, 1)

            traceBox = New TextBox With {
                .Dock = DockStyle.Fill,
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Vertical,
                .Font = New Font(FontFamily.GenericMonospace, 9.0F)
            }
            root.Controls.Add(traceBox, 0, 2)

            serviceTimer = New Timer With {.Interval = 10}
            AddHandler serviceTimer.Tick, AddressOf ServiceTimer_Tick
            AddHandler FormClosing, AddressOf MainForm_FormClosing

            RefreshPorts()
            Append("Ready. Connect a COM port, then start Beam Receive on the Palm or beam a file to it.")
        End Sub

        Private Sub RefreshPorts()
            Dim selected = TryCast(portCombo.SelectedItem, String)
            portCombo.Items.Clear()
            For Each portName In SerialPort.GetPortNames().OrderBy(Function(name) name, StringComparer.OrdinalIgnoreCase)
                portCombo.Items.Add(portName)
            Next
            If selected IsNot Nothing AndAlso portCombo.Items.Contains(selected) Then
                portCombo.SelectedItem = selected
            ElseIf portCombo.Items.Count > 0 Then
                portCombo.SelectedIndex = 0
            End If
        End Sub

        Private Sub ConnectButton_Click(sender As Object, e As EventArgs)
            If serial IsNot Nothing AndAlso serial.IsOpen Then
                Disconnect()
                Return
            End If

            Dim portName = TryCast(portCombo.SelectedItem, String)
            If String.IsNullOrWhiteSpace(portName) Then
                Append("No COM port selected.")
                Return
            End If

            Dim baud As Integer
            If Not Integer.TryParse(CStr(baudCombo.SelectedItem), baud) Then baud = 9600

            Try
                serial = New SerialPort(portName, baud, Parity.None, 8, StopBits.One) With {
                    .Handshake = Handshake.None,
                    .ReadTimeout = 1,
                    .WriteTimeout = 1000,
                    .DtrEnable = False,
                    .RtsEnable = False
                }
                AddHandler serial.DataReceived, AddressOf Serial_DataReceived
                serial.Open()
                ResetLinkState()
                serviceTimer.Start()
                portCombo.Enabled = False
                baudCombo.Enabled = False
                connectButton.Text = "Disconnect"
                beamButton.Enabled = True
                statusLabel.Text = $"Connected {portName} @ {baud}"
                Append($"Connected {portName} @ {baud} baud.")
            Catch ex As Exception
                Append($"Connect failed: {ex.Message}")
                Disconnect()
            End Try
        End Sub

        Private Sub Disconnect()
            serviceTimer.Stop()
            If serial IsNot Nothing Then
                Try
                    RemoveHandler serial.DataReceived, AddressOf Serial_DataReceived
                    If serial.IsOpen Then serial.Close()
                    serial.Dispose()
                Catch
                End Try
                serial = Nothing
            End If

            portCombo.Enabled = True
            baudCombo.Enabled = True
            connectButton.Text = "Connect"
            beamButton.Enabled = False
            statusLabel.Text = "Disconnected"
            Append("Disconnected.")
        End Sub

        Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs)
            Disconnect()
        End Sub

        Private Sub BrowseButton_Click(sender As Object, e As EventArgs)
            Using dialog As New FolderBrowserDialog()
                dialog.SelectedPath = captureBox.Text
                If dialog.ShowDialog(Me) = DialogResult.OK Then captureBox.Text = dialog.SelectedPath
            End Using
        End Sub

        Private Sub BeamButton_Click(sender As Object, e As EventArgs)
            If serial Is Nothing OrElse Not serial.IsOpen Then Return

            Using dialog As New OpenFileDialog()
                dialog.Title = "Beam file to Palm"
                dialog.Filter = "Palm beam files|*.pdb;*.prc;*.txt;*.vcf;*.vcs;*.bin|All files|*.*"
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

                Try
                    StartIrdaBeamSend(dialog.FileName)
                Catch ex As Exception
                    Append($"Beam send failed: {ex.Message}")
                    ResetIrdaBeamSender()
                End Try
            End Using
        End Sub

        Private Sub Serial_DataReceived(sender As Object, e As SerialDataReceivedEventArgs)
            Dim port = TryCast(sender, SerialPort)
            If port Is Nothing OrElse Not port.IsOpen Then Return

            Try
                Dim count = port.BytesToRead
                If count <= 0 Then Return
                Dim buffer(count - 1) As Byte
                Dim read = port.Read(buffer, 0, buffer.Length)
                SyncLock serialLock
                    For i = 0 To read - 1
                        rxBytes.Add(buffer(i))
                    Next
                End SyncLock
            Catch
            End Try
        End Sub

        Private Sub ServiceTimer_Tick(sender As Object, e As EventArgs)
            Dim bytes As Byte() = Array.Empty(Of Byte)()
            SyncLock serialLock
                If rxBytes.Count > 0 Then
                    bytes = rxBytes.ToArray()
                    rxBytes.Clear()
                End If
            End SyncLock

            If bytes.Length > 0 Then
                irdaTxQuietSinceTick = 0
                For Each value In bytes
                    ProcessIrdaByte(value)
                Next
            ElseIf serial IsNot Nothing AndAlso serial.IsOpen AndAlso serial.BytesToWrite = 0 AndAlso irdaTxQuietSinceTick = 0 Then
                irdaTxQuietSinceTick = Environment.TickCount64
            End If

            SendPendingIrdaResponse()
            ServiceIrdaBeamSender()
        End Sub

        Private Sub ProcessIrdaByte(value As Byte)
            Select Case value
                Case IrdaSirBof
                    irdaFrameBuffer.Clear()
                    irdaInFrame = True
                    irdaEscaped = False
                    Return
                Case IrdaSirEof
                    If irdaInFrame AndAlso irdaFrameBuffer.Count >= 4 Then
                        HandleIrdaFrame(irdaFrameBuffer.ToArray())
                        SendPendingIrdaResponse()
                    End If
                    irdaFrameBuffer.Clear()
                    irdaInFrame = False
                    irdaEscaped = False
                    Return
            End Select

            If Not irdaInFrame Then Return

            If irdaEscaped Then
                irdaFrameBuffer.Add(CByte(value Xor &H20))
                irdaEscaped = False
            ElseIf value = IrdaSirEscape Then
                irdaEscaped = True
            Else
                irdaFrameBuffer.Add(value)
            End If
        End Sub

        Private Sub HandleIrdaFrame(frame As Byte())
            If Not IrdaFrameHasValidFcs(frame) Then
                Append("RX frame with bad FCS ignored.")
                Return
            End If
            If irdaBeamState <> IrdaBeamSendState.Idle AndAlso HandleIrdaBeamSenderFrame(frame) Then Return

            If frame.Length >= 16 AndAlso frame(0) = IrdaBroadcastAddress AndAlso frame(1) = IrdaUiControl AndAlso frame(2) = IrdaXidFormat Then
                HandleIrdaXidFrame(frame)
                Return
            End If

            If frame.Length >= 13 AndAlso frame(1) = IrdaSnrmControl Then
                HandleIrdaSnrmFrame(frame)
                Return
            End If

            If frame.Length >= 4 AndAlso irdaConnectionAddress <> 0 AndAlso
                (frame(0) = irdaConnectionAddress OrElse frame(0) = (irdaConnectionAddress Or 1)) Then
                HandleIrdaConnectedFrame(frame)
            End If
        End Sub

        Private Sub HandleIrdaXidFrame(frame As Byte())
            Dim sourceAddress = ReadU32(frame, 3)
            Dim slot = frame(12)

            If sourceAddress <> irdaLastDiscoverySource Then
                irdaLastDiscoverySource = sourceAddress
                irdaRespondedInDiscovery = False
            End If

            If slot = &HFF Then
                irdaRespondedInDiscovery = False
                Return
            End If

            If irdaRespondedInDiscovery OrElse slot <> 2 Then Return
            SendIrdaXidResponse(sourceAddress, slot)
            irdaRespondedInDiscovery = True
        End Sub

        Private Sub HandleIrdaSnrmFrame(frame As Byte())
            Dim sourceAddress = ReadU32(frame, 2)
            Dim destinationAddress = ReadU32(frame, 6)
            If destinationAddress <> IrdaHostAddress Then Return

            Dim connectionAddress = frame(10)
            irdaConnectionAddress = CByte(connectionAddress And &HFE)
            irdaReceiveNext = 0
            irdaTransmitNext = 0
            irdaLastInformationResponse = Nothing
            irdaLastInformationResponseLabel = ""
            Dim parameterCount = Math.Max(0, frame.Length - 13)
            Dim parameters As Byte() = Array.Empty(Of Byte)()
            If parameterCount > 0 Then
                ReDim parameters(parameterCount - 1)
                Array.Copy(frame, 11, parameters, 0, parameterCount)
            End If

            Append($"RX link request peer=${sourceAddress:X8}.")
            SendIrdaUaResponse(sourceAddress, connectionAddress, NegotiateIrdaParameters(parameters))
        End Sub

        Private Sub HandleIrdaConnectedFrame(frame As Byte())
            Dim control = frame(1)
            If IsIrdaInformationControl(control) Then
                HandleIrdaInformationFrame(frame)
            ElseIf IsIrdaReceiveReadyControl(control) Then
                SendIrdaReceiveReady("RR")
            ElseIf control = IrdaDiscControl Then
                SendIrdaConnectedControl(IrdaUaResponseControl, "UA-DISC")
                ResetIrdaConnectionState()
            End If
        End Sub

        Private Sub HandleIrdaInformationFrame(frame As Byte())
            If frame.Length < 6 Then Return

            Dim control = frame(1)
            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence = irdaReceiveNext Then
                irdaReceiveNext = CByte((irdaReceiveNext + 1) And &H7)
                Dim dataLength = frame.Length - 4
                Dim data(dataLength - 1) As Byte
                Array.Copy(frame, 2, data, 0, dataLength)
                If HandleIrdaInformationPayload(data) Then Return
            ElseIf irdaLastInformationResponse IsNot Nothing Then
                QueueIrdaResponse(irdaLastInformationResponse, irdaLastInformationResponseLabel, IrdaConnectedTurnaroundDelayMs)
                Return
            End If

            SendIrdaReceiveReady("RR")
        End Sub

        Private Function HandleIrdaInformationPayload(data As Byte()) As Boolean
            If data.Length >= 4 AndAlso (data(0) And IrdaControlBit) <> 0 AndAlso data(2) = IrdaLmpConnectCommand Then
                Dim destinationLsap = CByte(data(0) And &H7F)
                Dim sourceLsap = CByte(data(1) And &H7F)
                Dim initialCredit As Byte = If(destinationLsap = IrdaObexLsap, CByte(&H10), CByte(0))
                Dim responseLength = If(initialCredit = 0, 4, 5)
                Dim response(responseLength - 1) As Byte
                response(0) = CByte(sourceLsap Or IrdaControlBit)
                response(1) = destinationLsap
                response(2) = IrdaLmpConnectConfirm
                response(3) = 0
                If responseLength > 4 Then response(4) = initialCredit
                SendIrdaInformationResponse(response, $"LMP-CONNECT-CNF dlsap=${sourceLsap:X2}")
                Return True
            End If

            If IsIrdaIasGetValueByClass(data) Then
                SendIrdaIasGetValueByClassResponse(data)
                Return True
            End If

            Return HandleIrdaTinyTpObexPayload(data)
        End Function

        Private Function HandleIrdaTinyTpObexPayload(data As Byte()) As Boolean
            If data.Length < 3 Then Return False
            Dim destinationLsap = CByte(data(0) And &H7F)
            Dim sourceLsap = CByte(data(1) And &H7F)
            If destinationLsap <> IrdaObexLsap Then Return False

            Dim obexOffset = 3
            If irdaObexExpectedLength > 0 Then
                If data.Length <= obexOffset Then Return False
                AppendObexFragment(data, obexOffset, data.Length - obexOffset)
                If irdaObexPacket.Count < irdaObexExpectedLength Then Return False

                Dim packet = irdaObexPacket.Take(irdaObexExpectedLength).ToArray()
                irdaObexPacket.Clear()
                irdaObexExpectedLength = 0
                Return HandleCompleteObexPacket(packet, irdaObexClientLsap)
            End If

            If data.Length - obexOffset < 3 Then Return False
            Dim packetLength = (CInt(data(obexOffset + 1)) << 8) Or data(obexOffset + 2)
            If packetLength < 3 Then Return False

            If obexOffset + packetLength > data.Length Then
                irdaObexExpectedLength = packetLength
                irdaObexClientLsap = sourceLsap
                irdaObexPacket.Clear()
                AppendObexFragment(data, obexOffset, data.Length - obexOffset)
                Return False
            End If

            Dim completePacket(packetLength - 1) As Byte
            Array.Copy(data, obexOffset, completePacket, 0, packetLength)
            Return HandleCompleteObexPacket(completePacket, sourceLsap)
        End Function

        Private Sub AppendObexFragment(data As Byte(), offset As Integer, count As Integer)
            For i = 0 To count - 1
                irdaObexPacket.Add(data(offset + i))
            Next
        End Sub

        Private Function HandleCompleteObexPacket(packet As Byte(), clientLsap As Byte) As Boolean
            If packet.Length < 3 Then Return False
            Dim opcode = packet(0)
            Dim packetLength = (CInt(packet(1)) << 8) Or packet(2)
            If packetLength < 3 OrElse packetLength > packet.Length Then Return False

            Select Case opcode
                Case &H80
                    irdaObexObjectName = ""
                    irdaObexObjectType = ""
                    irdaObexBody.Clear()
                    Dim response = New Byte() {clientLsap, IrdaObexLsap, &H10, &HA0, &H0, &H7, &H10, &H0, &H4, &H0}
                    SendIrdaInformationResponse(response, "OBEX CONNECT OK")
                    Return True
                Case &H81
                    SendIrdaObexResponse(clientLsap, &HA0, "OBEX DISCONNECT OK")
                    irdaObexObjectName = ""
                    irdaObexObjectType = ""
                    irdaObexBody.Clear()
                    irdaObexPacket.Clear()
                    irdaObexExpectedLength = 0
                    Return True
                Case &H2, &H82
                    HandleObexHeaders(packet, 3, packetLength)
                    If opcode = &H82 Then
                        SaveIrdaObexObject()
                        SendIrdaObexResponse(clientLsap, &HA0, "OBEX PUT OK")
                    Else
                        SendIrdaObexResponse(clientLsap, &H90, "OBEX PUT CONTINUE")
                    End If
                    Return True
                Case Else
                    Append($"OBEX opcode ${opcode:X2} ignored.")
                    Return False
            End Select
        End Function

        Private Sub SendIrdaObexResponse(clientLsap As Byte, responseCode As Byte, label As String)
            SendIrdaInformationResponse(New Byte() {clientLsap, IrdaObexLsap, &H10, responseCode, &H0, &H3}, label)
        End Sub

        Private Sub HandleObexHeaders(data As Byte(), startOffset As Integer, endOffset As Integer)
            Dim cursor = startOffset
            While cursor < endOffset
                Dim headerId = data(cursor)
                cursor += 1

                Select Case headerId And &HC0
                    Case &H0, &H40
                        If cursor + 2 > endOffset Then Exit While
                        Dim headerLength = (CInt(data(cursor)) << 8) Or data(cursor + 1)
                        cursor += 2
                        Dim valueLength = headerLength - 3
                        If valueLength < 0 OrElse cursor + valueLength > endOffset Then Exit While

                        If headerId = &H1 Then
                            irdaObexObjectName = DecodeObexUnicode(data, cursor, valueLength)
                            Append($"RX name: {irdaObexObjectName}")
                        ElseIf headerId = &H42 Then
                            irdaObexObjectType = DecodeObexAscii(data, cursor, valueLength)
                            Append($"RX type: {irdaObexObjectType}")
                        ElseIf headerId = &H48 OrElse headerId = &H49 Then
                            For i = 0 To valueLength - 1
                                irdaObexBody.Add(data(cursor + i))
                            Next
                            Append($"RX body bytes: {irdaObexBody.Count}")
                        End If
                        cursor += valueLength
                    Case &H80
                        cursor += 1
                    Case &HC0
                        cursor += 4
                End Select
            End While
        End Sub

        Private Sub SaveIrdaObexObject()
            Dim capturePath = captureBox.Text
            If String.IsNullOrWhiteSpace(capturePath) Then capturePath = Path.Combine(AppContext.BaseDirectory, "BeamCapture")
            Directory.CreateDirectory(capturePath)

            Dim fileName = If(String.IsNullOrWhiteSpace(irdaObexObjectName), $"beam_{DateTime.Now:yyyyMMdd_HHmmss}.bin", irdaObexObjectName)
            fileName = SanitizeFileName(fileName)
            Dim outputPath = UniquePath(Path.Combine(capturePath, fileName))
            File.WriteAllBytes(outputPath, irdaObexBody.ToArray())
            Append($"Saved {Path.GetFileName(outputPath)} ({irdaObexBody.Count} bytes).")
        End Sub

        Private Sub SendIrdaXidResponse(destinationAddress As UInteger, slot As Byte)
            Dim nameBytes = Encoding.ASCII.GetBytes("PalmBeamCOM")
            Dim payload(13 + 4 + nameBytes.Length - 1) As Byte
            payload(0) = IrdaDiscoveryResponseAddress
            payload(1) = IrdaXidResponseControl
            payload(2) = IrdaXidFormat
            WriteU32(payload, 3, IrdaHostAddress)
            WriteU32(payload, 7, destinationAddress)
            payload(11) = 0
            payload(12) = slot
            payload(13) = 0
            payload(14) = &H82
            payload(15) = &H20
            payload(16) = 0
            Array.Copy(nameBytes, 0, payload, 17, nameBytes.Length)

            pendingIrdaResponse = BuildIrdaSirFrame(payload)
            pendingIrdaResponseLabel = $"XID slot=${slot:X2}"
            pendingIrdaResponseDueTick = Environment.TickCount64 + IrdaTurnaroundDelayMs
        End Sub

        Private Sub SendIrdaUaResponse(destinationAddress As UInteger, connectionAddress As Byte, parameters As Byte())
            Dim payload(10 + parameters.Length - 1) As Byte
            payload(0) = connectionAddress
            payload(1) = IrdaUaResponseControl
            WriteU32(payload, 2, IrdaHostAddress)
            WriteU32(payload, 6, destinationAddress)
            If parameters.Length > 0 Then Array.Copy(parameters, 0, payload, 10, parameters.Length)

            pendingIrdaResponse = BuildIrdaSirFrame(payload)
            pendingIrdaResponseLabel = $"UA ca=${connectionAddress:X2}"
            pendingIrdaResponseDueTick = Environment.TickCount64 + IrdaTurnaroundDelayMs
        End Sub

        Private Shared Function NegotiateIrdaParameters(requested As Byte()) As Byte()
            If requested Is Nothing OrElse requested.Length = 0 Then Return Array.Empty(Of Byte)()

            Dim negotiated As Byte() = CType(requested.Clone(), Byte())
            Dim index = 0
            While index + 2 < negotiated.Length
                Dim pi = negotiated(index)
                Dim length = negotiated(index + 1)
                Dim valueIndex = index + 2
                If valueIndex + length > negotiated.Length Then Exit While

                Select Case pi
                    Case &H1
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H2)
                    Case &H83
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H1)
                    Case &H84
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H1)
                End Select

                index += 2 + length
            End While

            Return negotiated
        End Function

        Private Sub SendIrdaConnectedControl(control As Byte, label As String)
            If irdaConnectionAddress = 0 Then Return
            QueueIrdaResponse(BuildIrdaSirFrame(New Byte() {irdaConnectionAddress, control}), $"{label} ca=${irdaConnectionAddress:X2}", IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Sub SendIrdaReceiveReady(label As String)
            Dim control = CByte(IrdaRrPollFinalControl Or ((irdaReceiveNext And &H7) << 5))
            SendIrdaConnectedControl(control, label)
        End Sub

        Private Sub SendIrdaInformationResponse(data As Byte(), label As String)
            If irdaConnectionAddress = 0 Then Return

            Dim control = CByte(IrdaFinalBit Or ((irdaTransmitNext And &H7) << 1) Or ((irdaReceiveNext And &H7) << 5))
            Dim payload(2 + data.Length - 1) As Byte
            payload(0) = irdaConnectionAddress
            payload(1) = control
            Array.Copy(data, 0, payload, 2, data.Length)

            Dim frame = BuildIrdaSirFrame(payload)
            Dim responseLabel = $"{label} ctrl=${control:X2}"
            irdaTransmitNext = CByte((irdaTransmitNext + 1) And &H7)
            irdaLastInformationResponse = frame
            irdaLastInformationResponseLabel = responseLabel
            QueueIrdaResponse(frame, responseLabel, IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Sub QueueIrdaResponse(frame As Byte(), label As String, delayMs As Long)
            pendingIrdaResponses.Enqueue(New IrdaPendingResponse(frame, label, Environment.TickCount64 + delayMs))
        End Sub

        Private Sub SendPendingIrdaResponse()
            If serial Is Nothing OrElse Not serial.IsOpen Then Return
            If pendingIrdaResponse Is Nothing AndAlso pendingIrdaResponses.Count > 0 Then
                Dim queued = pendingIrdaResponses.Dequeue()
                pendingIrdaResponse = queued.Frame
                pendingIrdaResponseLabel = queued.Label
                pendingIrdaResponseDueTick = queued.Due
            End If

            If pendingIrdaResponse Is Nothing OrElse pendingIrdaResponse.Length = 0 Then Return
            If serial.BytesToWrite <> 0 Then Return
            Dim nowTick = Environment.TickCount64
            If irdaTxQuietSinceTick = 0 Then
                irdaTxQuietSinceTick = nowTick
                Return
            End If
            If nowTick - irdaTxQuietSinceTick < IrdaTxQuietBeforeResponseMs OrElse nowTick < pendingIrdaResponseDueTick Then Return

            Dim response = pendingIrdaResponse
            Dim label = pendingIrdaResponseLabel
            pendingIrdaResponse = Nothing
            pendingIrdaResponseLabel = ""
            pendingIrdaResponseDueTick = 0
            WriteSerial(response, label)
        End Sub

        Private Sub StartIrdaBeamSend(filePath As String)
            ResetIrdaBeamSender()
            ResetIrdaConnectionState()
            pendingIrdaResponse = Nothing
            pendingIrdaResponses.Clear()

            irdaBeamFileName = Path.GetFileName(filePath)
            irdaBeamFileBytes = File.ReadAllBytes(filePath)
            If Path.GetExtension(irdaBeamFileName).Equals(".txt", StringComparison.OrdinalIgnoreCase) Then
                irdaBeamFileBytes = NormalizeIrdaBeamTextBytes(irdaBeamFileBytes)
                If irdaBeamFileBytes.Length > PalmMemoMaxBytes Then
                    Throw New InvalidOperationException($"Memo text is too large ({irdaBeamFileBytes.Length} bytes). Limit is {PalmMemoMaxBytes} bytes.")
                End If
            End If

            irdaBeamConnectionAddress = &HC0
            irdaBeamReceiveNext = 0
            irdaBeamTransmitNext = 0
            irdaBeamOffset = 0
            irdaBeamDiscoverySlot = -1
            irdaBeamEmptyEndBodySent = False
            irdaBeamState = IrdaBeamSendState.Discover
            irdaBeamLastProgressTick = 0
            Append($"Beam send starting: {irdaBeamFileName} ({irdaBeamFileBytes.Length} bytes).")
            SendIrdaBeamDiscovery()
        End Sub

        Private Sub ServiceIrdaBeamSender()
            If irdaBeamState = IrdaBeamSendState.Idle OrElse serial Is Nothing OrElse Not serial.IsOpen Then Return
            Dim nowTick = Environment.TickCount64
            If irdaBeamState = IrdaBeamSendState.Discover Then
                If irdaBeamLastProgressTick = 0 OrElse nowTick - irdaBeamLastProgressTick >= IrdaBeamDiscoveryRetryMs Then
                    SendIrdaBeamDiscovery()
                End If
                Return
            End If

            If irdaBeamLastProgressTick <> 0 AndAlso nowTick - irdaBeamLastProgressTick >= IrdaBeamTimeoutMs Then
                Append($"Beam send timed out at {irdaBeamState}.")
                ResetIrdaBeamSender()
            End If
        End Sub

        Private Function HandleIrdaBeamSenderFrame(frame As Byte()) As Boolean
            If irdaBeamState = IrdaBeamSendState.Discover AndAlso
                frame.Length >= 13 AndAlso frame(0) = IrdaDiscoveryResponseAddress AndAlso frame(1) = IrdaXidResponseControl Then
                irdaBeamPeerAddress = ReadU32(frame, 3)
                irdaBeamLastProgressTick = Environment.TickCount64
                Append($"Beam peer found: ${irdaBeamPeerAddress:X8}.")
                irdaBeamState = IrdaBeamSendState.Snrm
                SendIrdaBeamSnrm()
                Return True
            End If

            If irdaBeamState = IrdaBeamSendState.Snrm AndAlso frame.Length >= 2 AndAlso frame(1) = IrdaUaResponseControl Then
                irdaBeamReceiveNext = 0
                irdaBeamTransmitNext = 0
                irdaBeamLastProgressTick = Environment.TickCount64
                Append("Beam link established.")
                irdaBeamState = IrdaBeamSendState.IasConnect
                SendIrdaBeamInformation(BuildLmpConnectPayload(0, 1, 0), "IAS-CONNECT")
                Return True
            End If

            If frame(0) <> irdaBeamConnectionAddress AndAlso frame(0) <> (irdaBeamConnectionAddress Or 1) Then Return False
            Dim control = frame(1)
            If IsIrdaReceiveReadyControl(control) Then
                irdaBeamLastProgressTick = Environment.TickCount64
                Return True
            End If
            If control = IrdaDiscControl Then
                SendIrdaBeamControl(IrdaUaResponseControl, "BEAM-UA-DISC")
                ResetIrdaBeamSender()
                Return True
            End If
            If Not IsIrdaInformationControl(control) OrElse frame.Length < 6 Then Return True

            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence = irdaBeamReceiveNext Then irdaBeamReceiveNext = CByte((irdaBeamReceiveNext + 1) And &H7)
            Dim dataLength = frame.Length - 4
            Dim data(dataLength - 1) As Byte
            Array.Copy(frame, 2, data, 0, dataLength)
            HandleIrdaBeamSenderPayload(data)
            Return True
        End Function

        Private Sub HandleIrdaBeamSenderPayload(data As Byte())
            irdaBeamLastProgressTick = Environment.TickCount64
            Select Case irdaBeamState
                Case IrdaBeamSendState.IasConnect
                    If data.Length >= 4 AndAlso (data(0) And &H7F) = 1 AndAlso data(2) = IrdaLmpConnectConfirm Then
                        Append("Beam IAS connected; connecting OBEX.")
                        irdaBeamState = IrdaBeamSendState.ObexLmpConnect
                        SendIrdaBeamInformation(BuildLmpConnectPayload(IrdaObexLsap, 3, &H10), "OBEX-LMP-CONNECT")
                    End If
                Case IrdaBeamSendState.ObexLmpConnect
                    If data.Length >= 4 AndAlso data(2) = IrdaLmpConnectConfirm Then
                        irdaBeamState = IrdaBeamSendState.ObexConnect
                        SendIrdaBeamInformation(WrapTinyTpObex(BuildObexConnectPacket()), "OBEX-CONNECT")
                    End If
                Case IrdaBeamSendState.ObexConnect
                    If IsObexResponse(data, &HA0) Then
                        Append("Beam OBEX connected; sending metadata.")
                        irdaBeamState = IrdaBeamSendState.ObexPutMeta
                        SendIrdaBeamPutMetadata()
                    End If
                Case IrdaBeamSendState.ObexPutMeta
                    If IsObexResponse(data, &H90) OrElse IsObexResponse(data, &HA0) Then
                        irdaBeamState = IrdaBeamSendState.ObexPutBody
                        SendIrdaBeamNextBodyChunk()
                    End If
                Case IrdaBeamSendState.ObexPutBody
                    If IsObexResponse(data, &H90) Then
                        SendIrdaBeamNextBodyChunk()
                    ElseIf IsObexResponse(data, &HA0) Then
                        Append("Beam body accepted; disconnecting.")
                        irdaBeamState = IrdaBeamSendState.ObexDisconnect
                        SendIrdaBeamInformation(WrapTinyTpObex(New Byte() {&H81, &H0, &H3}), "OBEX-DISCONNECT")
                    End If
                Case IrdaBeamSendState.ObexDisconnect
                    If IsObexResponse(data, &HA0) Then
                        irdaBeamState = IrdaBeamSendState.LinkDisconnect
                        SendIrdaBeamControl(IrdaDiscControl, "BEAM-DISC")
                    End If
            End Select
        End Sub

        Private Sub SendIrdaBeamDiscovery()
            Dim slot = irdaBeamDiscoverySlot
            If slot < 0 OrElse slot > 5 Then slot = 0
            irdaBeamDiscoverySlot = slot + 1

            Dim payload(13) As Byte
            payload(0) = IrdaBroadcastAddress
            payload(1) = IrdaUiControl
            payload(2) = IrdaXidFormat
            WriteU32(payload, 3, IrdaHostAddress)
            WriteU32(payload, 7, &HFFFFFFFFUI)
            payload(11) = CByte((slot << 4) Or &H6)
            payload(12) = CByte(slot)
            payload(13) = 0
            SendIrdaBeamPayload(payload, $"BEAM-XID slot=${payload(12):X2}")
        End Sub

        Private Sub SendIrdaBeamSnrm()
            Dim parameters = New Byte() {
                &H1, &H1, &H2,
                &H82, &H1, &H1,
                &H83, &H1, &H1,
                &H84, &H1, &H1
            }
            Dim payload(11 + parameters.Length - 1) As Byte
            payload(0) = IrdaBroadcastAddress
            payload(1) = IrdaSnrmControl
            WriteU32(payload, 2, IrdaHostAddress)
            WriteU32(payload, 6, irdaBeamPeerAddress)
            payload(10) = irdaBeamConnectionAddress
            Array.Copy(parameters, 0, payload, 11, parameters.Length)
            SendIrdaBeamPayload(payload, "BEAM-SNRM")
        End Sub

        Private Sub SendIrdaBeamInformation(data As Byte(), label As String)
            Dim control = CByte(IrdaFinalBit Or ((irdaBeamTransmitNext And &H7) << 1) Or ((irdaBeamReceiveNext And &H7) << 5))
            Dim payload(2 + data.Length - 1) As Byte
            payload(0) = CByte(irdaBeamConnectionAddress Or 1)
            payload(1) = control
            Array.Copy(data, 0, payload, 2, data.Length)
            irdaBeamTransmitNext = CByte((irdaBeamTransmitNext + 1) And &H7)
            SendIrdaBeamPayload(payload, $"{label} ctrl=${control:X2}")
        End Sub

        Private Sub SendIrdaBeamControl(control As Byte, label As String)
            SendIrdaBeamPayload(New Byte() {CByte(irdaBeamConnectionAddress Or 1), control}, label)
            If control = IrdaDiscControl Then
                Append($"Beam send complete: {irdaBeamFileName}")
                ResetIrdaBeamSender()
            End If
        End Sub

        Private Sub SendIrdaBeamPayload(payload As Byte(), label As String)
            Dim frame = BuildIrdaSirFrame(payload)
            WriteSerial(frame, label)
            irdaBeamLastProgressTick = Environment.TickCount64
        End Sub

        Private Sub SendIrdaBeamPutMetadata()
            Dim headers As New List(Of Byte)
            headers.AddRange(BuildObexNameHeader(irdaBeamFileName))
            headers.AddRange(BuildObexLengthHeader(If(irdaBeamFileBytes Is Nothing, 0, irdaBeamFileBytes.Length)))
            If IsIrdaBeamTextFile() Then
                headers.AddRange(BuildObexUnicodeHeader(&H5, Path.GetFileNameWithoutExtension(irdaBeamFileName)))
            End If

            Dim packet = BuildObexPacket(&H2, headers.ToArray())
            SendIrdaBeamInformation(WrapTinyTpObex(packet), "OBEX-PUT-META")
        End Sub

        Private Sub SendIrdaBeamNextBodyChunk()
            If irdaBeamFileBytes Is Nothing Then Return
            Dim remaining = irdaBeamFileBytes.Length - irdaBeamOffset
            If remaining <= 0 Then
                If IsIrdaBeamTextFile() AndAlso Not irdaBeamEmptyEndBodySent Then
                    irdaBeamEmptyEndBodySent = True
                    SendIrdaBeamInformation(WrapTinyTpObex(BuildObexPacket(&H82, BuildObexBodyHeader(&H49, Array.Empty(Of Byte)()))), "OBEX-PUT-END")
                    Return
                End If
                irdaBeamState = IrdaBeamSendState.ObexDisconnect
                SendIrdaBeamInformation(WrapTinyTpObex(New Byte() {&H81, &H0, &H3}), "OBEX-DISCONNECT")
                Return
            End If

            Dim chunk = Math.Min(IrdaBeamBodyChunkSize, remaining)
            Dim body(chunk - 1) As Byte
            Array.Copy(irdaBeamFileBytes, irdaBeamOffset, body, 0, chunk)
            irdaBeamOffset += chunk
            Dim finalChunk = irdaBeamOffset >= irdaBeamFileBytes.Length
            Dim opcode As Byte = If(finalChunk, CByte(&H82), CByte(&H2))
            Dim headerId As Byte = If(finalChunk, CByte(&H49), CByte(&H48))
            SendIrdaBeamInformation(WrapTinyTpObex(BuildObexPacket(opcode, BuildObexBodyHeader(headerId, body))), $"OBEX-PUT-BODY {irdaBeamOffset}/{irdaBeamFileBytes.Length}")
        End Sub

        Private Sub ResetLinkState()
            SyncLock serialLock
                rxBytes.Clear()
            End SyncLock
            irdaFrameBuffer.Clear()
            irdaInFrame = False
            irdaEscaped = False
            pendingIrdaResponse = Nothing
            pendingIrdaResponses.Clear()
            ResetIrdaConnectionState()
            ResetIrdaBeamSender()
        End Sub

        Private Sub ResetIrdaConnectionState()
            irdaConnectionAddress = 0
            irdaReceiveNext = 0
            irdaTransmitNext = 0
            irdaLastInformationResponse = Nothing
            irdaLastInformationResponseLabel = ""
            irdaObexObjectName = ""
            irdaObexObjectType = ""
            irdaObexBody.Clear()
            irdaObexPacket.Clear()
            irdaObexExpectedLength = 0
            irdaObexClientLsap = 0
            irdaTxQuietSinceTick = 0
        End Sub

        Private Sub ResetIrdaBeamSender()
            irdaBeamState = IrdaBeamSendState.Idle
            irdaBeamPeerAddress = 0
            irdaBeamConnectionAddress = 0
            irdaBeamReceiveNext = 0
            irdaBeamTransmitNext = 0
            irdaBeamFileName = ""
            irdaBeamFileBytes = Nothing
            irdaBeamOffset = 0
            irdaBeamLastProgressTick = 0
            irdaBeamDiscoverySlot = -1
            irdaBeamEmptyEndBodySent = False
        End Sub

        Private Sub WriteSerial(bytes As Byte(), label As String)
            If serial Is Nothing OrElse Not serial.IsOpen OrElse bytes Is Nothing OrElse bytes.Length = 0 Then Return
            Try
                serial.Write(bytes, 0, bytes.Length)
                If Not label.StartsWith("BEAM-XID", StringComparison.Ordinal) Then Append($"TX {label} ({bytes.Length} bytes).")
            Catch ex As Exception
                Append($"TX failed: {ex.Message}")
            End Try
        End Sub

        Private Shared Function BuildLmpConnectPayload(destinationLsap As Byte, sourceLsap As Byte, initialCredit As Byte) As Byte()
            If initialCredit = 0 Then
                Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0}
            End If
            Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0, initialCredit}
        End Function

        Private Shared Function WrapTinyTpObex(obexPacket As Byte()) As Byte()
            Dim payload(3 + obexPacket.Length - 1) As Byte
            payload(0) = IrdaObexLsap
            payload(1) = 3
            payload(2) = &H10
            Array.Copy(obexPacket, 0, payload, 3, obexPacket.Length)
            Return payload
        End Function

        Private Shared Function BuildObexConnectPacket() As Byte()
            Return New Byte() {&H80, &H0, &H7, &H10, &H0, &H4, &H0}
        End Function

        Private Shared Function BuildObexPacket(opcode As Byte, headers As Byte()) As Byte()
            Dim safeHeaders = If(headers, Array.Empty(Of Byte)())
            Dim packet(3 + safeHeaders.Length - 1) As Byte
            packet(0) = opcode
            WriteU16(packet, 1, CUShort(packet.Length))
            Array.Copy(safeHeaders, 0, packet, 3, safeHeaders.Length)
            Return packet
        End Function

        Private Shared Function BuildObexUnicodeHeader(headerId As Byte, value As String) As Byte()
            Dim chars = If(value, "").ToCharArray()
            Dim encoded((chars.Length + 1) * 2 - 1) As Byte
            Dim cursor = 0
            For Each ch In chars
                Dim code = AscW(ch)
                encoded(cursor) = CByte((code >> 8) And &HFF)
                encoded(cursor + 1) = CByte(code And &HFF)
                cursor += 2
            Next
            Return BuildObexVariableHeader(headerId, encoded)
        End Function

        Private Shared Function BuildObexNameHeader(name As String) As Byte()
            Return BuildObexUnicodeHeader(&H1, name)
        End Function

        Private Shared Function BuildObexBodyHeader(headerId As Byte, bytes As Byte()) As Byte()
            Return BuildObexVariableHeader(headerId, If(bytes, Array.Empty(Of Byte)()))
        End Function

        Private Shared Function BuildObexVariableHeader(headerId As Byte, value As Byte()) As Byte()
            Dim safeValue = If(value, Array.Empty(Of Byte)())
            Dim header(3 + safeValue.Length - 1) As Byte
            header(0) = headerId
            WriteU16(header, 1, CUShort(header.Length))
            Array.Copy(safeValue, 0, header, 3, safeValue.Length)
            Return header
        End Function

        Private Shared Function BuildObexLengthHeader(length As Integer) As Byte()
            Return New Byte() {&HC3, CByte((length >> 24) And &HFF), CByte((length >> 16) And &HFF), CByte((length >> 8) And &HFF), CByte(length And &HFF)}
        End Function

        Private Shared Function IsObexResponse(data As Byte(), responseCode As Byte) As Boolean
            If data.Length < 6 Then Return False
            Dim cursor = 0
            If data.Length >= 3 AndAlso ((data(0) And &H7F) = IrdaObexLsap OrElse (data(1) And &H7F) = IrdaObexLsap) Then cursor = 3
            Return data.Length > cursor AndAlso data(cursor) = responseCode
        End Function

        Private Function IsIrdaBeamTextFile() As Boolean
            Return Path.GetExtension(If(irdaBeamFileName, "")).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function NormalizeIrdaBeamTextBytes(bytes As Byte()) As Byte()
            Dim text = Encoding.Default.GetString(If(bytes, Array.Empty(Of Byte)()))
            text = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, vbCrLf)
            Return Encoding.Default.GetBytes(text)
        End Function

        Private Shared Function IsIrdaInformationControl(control As Byte) As Boolean
            Return (control And &H1) = 0
        End Function

        Private Shared Function IsIrdaReceiveReadyControl(control As Byte) As Boolean
            Return (control And &H1F) = IrdaRrPollFinalControl
        End Function

        Private Shared Function IsIrdaIasGetValueByClass(data As Byte()) As Boolean
            If data.Length < 5 Then Return False
            Return (data(0) And &H7F) = 0 AndAlso (data(2) And &H7F) = IrdaIasGetValueByClass
        End Function

        Private Sub SendIrdaIasGetValueByClassResponse(request As Byte())
            Dim className = ""
            Dim attributeName = ""
            Dim cursor = 3
            If cursor < request.Length Then
                Dim classLength = request(cursor)
                cursor += 1
                If cursor + classLength <= request.Length Then
                    className = Encoding.ASCII.GetString(request, cursor, classLength)
                    cursor += classLength
                End If
            End If
            If cursor < request.Length Then
                Dim attributeLength = request(cursor)
                cursor += 1
                If cursor + attributeLength <= request.Length Then attributeName = Encoding.ASCII.GetString(request, cursor, attributeLength)
            End If

            Dim clientLsap = CByte(request(1) And &H7F)
            Dim response(12) As Byte
            response(0) = clientLsap
            response(1) = 0
            response(2) = CByte(IrdaIasGetValueByClass Or IrdaIasLast)
            response(3) = IrdaIasSuccess
            response(4) = 0
            response(5) = 1
            response(6) = 0
            response(7) = 0
            response(8) = IrdaIasInteger
            response(9) = 0
            response(10) = 0
            response(11) = 0
            response(12) = IrdaObexLsap
            SendIrdaInformationResponse(response, $"IAS {className}/{attributeName} LSAP=${IrdaObexLsap:X2}")
        End Sub

        Private Shared Function BuildIrdaSirFrame(payload As Byte()) As Byte()
            Dim bytes As New List(Of Byte)
            For i = 1 To 10
                bytes.Add(&HFF)
            Next
            bytes.Add(IrdaSirBof)

            Dim crc = Crc16X25(payload, payload.Length)
            For Each value In payload
                AddEscapedIrdaByte(bytes, value)
            Next
            AddEscapedIrdaByte(bytes, CByte(crc And &HFF))
            AddEscapedIrdaByte(bytes, CByte((crc >> 8) And &HFF))
            bytes.Add(IrdaSirEof)
            Return bytes.ToArray()
        End Function

        Private Shared Sub AddEscapedIrdaByte(bytes As List(Of Byte), value As Byte)
            If value = IrdaSirBof OrElse value = IrdaSirEof OrElse value = IrdaSirEscape Then
                bytes.Add(IrdaSirEscape)
                bytes.Add(CByte(value Xor &H20))
            Else
                bytes.Add(value)
            End If
        End Sub

        Private Shared Function IrdaFrameHasValidFcs(frame As Byte()) As Boolean
            If frame.Length < 3 Then Return False
            Dim expected = CUInt(frame(frame.Length - 2)) Or (CUInt(frame(frame.Length - 1)) << 8)
            Dim actual = CUInt(Crc16X25(frame, frame.Length - 2))
            Return expected = actual
        End Function

        Private Shared Function Crc16X25(buffer As Byte(), count As Integer) As Integer
            Dim crc As Integer = &HFFFF
            For i = 0 To count - 1
                crc = crc Xor buffer(i)
                For bit = 0 To 7
                    If (crc And 1) <> 0 Then
                        crc = ((crc >> 1) Xor &H8408) And &HFFFF
                    Else
                        crc = (crc >> 1) And &HFFFF
                    End If
                Next
            Next
            Return (Not crc) And &HFFFF
        End Function

        Private Shared Function ReadU32(buffer As Byte(), offset As Integer) As UInteger
            Return (CUInt(buffer(offset)) << 24) Or
                (CUInt(buffer(offset + 1)) << 16) Or
                (CUInt(buffer(offset + 2)) << 8) Or
                CUInt(buffer(offset + 3))
        End Function

        Private Shared Sub WriteU16(buffer As Byte(), offset As Integer, value As UShort)
            buffer(offset) = CByte((value >> 8) And &HFFUS)
            buffer(offset + 1) = CByte(value And &HFFUS)
        End Sub

        Private Shared Sub WriteU32(buffer As Byte(), offset As Integer, value As UInteger)
            buffer(offset) = CByte((value >> 24) And &HFFUI)
            buffer(offset + 1) = CByte((value >> 16) And &HFFUI)
            buffer(offset + 2) = CByte((value >> 8) And &HFFUI)
            buffer(offset + 3) = CByte(value And &HFFUI)
        End Sub

        Private Shared Function DecodeObexUnicode(data As Byte(), offset As Integer, count As Integer) As String
            Dim builder As New StringBuilder()
            Dim endOffset = offset + count
            Dim cursor = offset
            While cursor + 1 < endOffset
                Dim code = (CInt(data(cursor)) << 8) Or data(cursor + 1)
                If code = 0 Then Exit While
                builder.Append(ChrW(code))
                cursor += 2
            End While
            Return builder.ToString()
        End Function

        Private Shared Function DecodeObexAscii(data As Byte(), offset As Integer, count As Integer) As String
            Dim endOffset = offset + count
            If count > 0 AndAlso offset + count <= data.Length AndAlso data(offset + count - 1) = 0 Then endOffset -= 1
            If endOffset <= offset Then Return ""
            Return Encoding.ASCII.GetString(data, offset, endOffset - offset)
        End Function

        Private Shared Function UniquePath(filePath As String) As String
            If Not File.Exists(filePath) Then Return filePath
            Dim directory = Path.GetDirectoryName(filePath)
            Dim baseName = Path.GetFileNameWithoutExtension(filePath)
            Dim extension = Path.GetExtension(filePath)
            Dim suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
            Dim candidate = Path.Combine(directory, $"{baseName}_{suffix}{extension}")
            If Not File.Exists(candidate) Then Return candidate
            Dim index = 2
            Do
                candidate = Path.Combine(directory, $"{baseName}_{suffix}_{index:D2}{extension}")
                If Not File.Exists(candidate) Then Return candidate
                index += 1
            Loop
        End Function

        Private Shared Function SanitizeFileName(fileName As String) As String
            Dim invalid = Path.GetInvalidFileNameChars()
            Dim sb As New StringBuilder(fileName.Length)
            For Each ch In fileName
                sb.Append(If(invalid.Contains(ch), "_"c, ch))
            Next
            Dim sanitized = sb.ToString().Trim()
            If sanitized.Length = 0 Then sanitized = "beam.bin"
            Return sanitized
        End Function

        Private Sub Append(message As String)
            Dim line = $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"
            traceBox.AppendText(line)
        End Sub
    End Class
End Namespace
