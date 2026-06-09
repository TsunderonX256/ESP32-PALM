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
        Private ReadOnly fullDiagnosticsCheck As CheckBox
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
        Private irdaLastInformationResponseData As Byte()
        Private irdaLastInformationResponseSendSequence As Byte
        Private irdaLastInformationResponseLabel As String = ""
        Private irdaLastInformationResponseAcked As Boolean
        Private irdaObexObjectName As String = ""
        Private irdaObexObjectType As String = ""
        Private irdaObexObjectLength As Integer = -1
        Private irdaObexLastProgressPercent As Integer = -1
        Private ReadOnly irdaObexBody As New List(Of Byte)
        Private ReadOnly irdaObexPacket As New List(Of Byte)
        Private irdaObexExpectedLength As Integer
        Private irdaObexClientLsap As Byte
        Private irdaObexFragmentOffset As Integer = 3
        Private irdaObexLastFragmentLogCount As Integer
        Private irdaTinyTpPacketsSinceCreditGrant As Integer
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
        Private irdaBeamRemoteObexLsap As Byte = IrdaObexLsap
        Private irdaBeamIasQueryIndex As Integer
        Private irdaBeamLastBodyProgressPercent As Integer = -1
        Private irdaBeamBodyChunkSize As Integer = IrdaBeamDefaultBodyChunkSize
        Private irdaBeamLastInformationFrame As Byte()
        Private irdaBeamLastInformationLabel As String = ""

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
        Private Const IrdaLinkSetupTurnaroundDelayMs As Long = 0
        Private Const IrdaConnectedTurnaroundDelayMs As Long = 0
        Private Const IrdaTxQuietBeforeResponseMs As Long = 2
        Private Const IrdaTinyTpCreditGrant As Byte = &H10
        Private Const IrdaTinyTpCreditLowWatermark As Integer = 8
        Private Const IrdaBeamDefaultBodyChunkSize As Integer = 40
        Private Const IrdaBeamMaxBodyChunkSize As Integer = 224
        Private Const IrdaBeamBodyPacketOverhead As Integer = 9
        Private Const IrdaIFieldSize64Bit As Byte = &H1
        Private Const IrdaIFieldSize128Bit As Byte = &H2
        Private Const IrdaIFieldSize256Bit As Byte = &H4
        Private Const IrdaBeamOfferedIFieldSizeMask As Byte = IrdaIFieldSize64Bit Or IrdaIFieldSize128Bit Or IrdaIFieldSize256Bit
        Private Const IrdaBeamDiscoveryRetryMs As Long = 250
        Private Const IrdaBeamTimeoutMs As Long = 15000
        Private Const PalmMemoMaxBytes As Integer = 4096
        Private Shared ReadOnly IrdaObexIasClasses As String() = {"OBEX", "OBEX:IrXfer", "OBEX:Default", "OBEX:Inbox"}

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
            IasQuery
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
            baudCombo.SelectedItem = "115200"
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
            fullDiagnosticsCheck = New CheckBox With {
                .Text = "Full diagnostics",
                .AutoSize = True,
                .Margin = New Padding(12, 6, 0, 0)
            }
            portRow.Controls.Add(fullDiagnosticsCheck)
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
            If Not Integer.TryParse(CStr(baudCombo.SelectedItem), baud) Then baud = 115200

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
                AppendDiag("RX frame with bad FCS ignored.")
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
                Return
            End If

            If fullDiagnosticsCheck.Checked Then
                Append($"RX ignored frame addr=${frame(0):X2} ctrl=${If(frame.Length > 1, frame(1).ToString("X2", CultureInfo.InvariantCulture), "--")} len={Math.Max(0, frame.Length - 2)} data={FormatBytes(frame.Take(Math.Min(frame.Length, 32)))}")
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
            irdaLastInformationResponseData = Nothing
            irdaLastInformationResponseSendSequence = 0
            irdaLastInformationResponseLabel = ""
            irdaLastInformationResponseAcked = False
            Dim parameterCount = Math.Max(0, frame.Length - 13)
            Dim parameters As Byte() = Array.Empty(Of Byte)()
            If parameterCount > 0 Then
                ReDim parameters(parameterCount - 1)
                Array.Copy(frame, 11, parameters, 0, parameterCount)
            End If

            Dim negotiatedParameters = NegotiateIrdaParameters(parameters)
            AppendDiag($"RX link request peer=${sourceAddress:X8} ca=${connectionAddress:X2} params={FormatIrdaParameters(parameters)}.")
            AppendDiag($"TX link response params={FormatIrdaParameters(negotiatedParameters)}.")
            SendIrdaUaResponse(sourceAddress, connectionAddress, negotiatedParameters)
        End Sub

        Private Sub HandleIrdaConnectedFrame(frame As Byte())
            Dim control = frame(1)
            If ShouldLogIrdaControlFrame(control) Then
                AppendDiag($"RX link ctrl=${control:X2} ns={(control >> 1) And &H7} nr={(control >> 5) And &H7} len={Math.Max(0, frame.Length - 4)}.")
            End If

            If IsIrdaInformationControl(control) Then
                HandleIrdaInformationFrame(frame)
            ElseIf IsIrdaReceiveReadyControl(control) Then
                HandleIrdaReceiveReadyFrame(control)
            ElseIf control = IrdaDiscControl Then
                AppendDiag("RX link disconnect.")
                SendIrdaConnectedControl(IrdaUaResponseControl, "UA-DISC")
                ResetIrdaConnectionState()
            End If
        End Sub

        Private Function ShouldLogIrdaControlFrame(control As Byte) As Boolean
            If fullDiagnosticsCheck.Checked Then Return True
            If control = IrdaDiscControl Then Return True
            Return IsIrdaInformationControl(control)
        End Function

        Private Sub HandleIrdaReceiveReadyFrame(control As Byte)
            HandleIrdaReceiveAck(control, True)
        End Sub

        Private Sub HandleIrdaReceiveAck(control As Byte, sendIdleReceiveReady As Boolean)
            Dim receiveAck = CByte((control >> 5) And &H7)
            If receiveAck = irdaTransmitNext Then
                If irdaLastInformationResponse IsNot Nothing Then
                    irdaLastInformationResponseAcked = True
                ElseIf sendIdleReceiveReady Then
                    SendIrdaReceiveReady("RR")
                End If
                Return
            End If

            If irdaLastInformationResponse IsNot Nothing Then
                QueueIrdaInformationRetransmit($"{irdaLastInformationResponseLabel} RETX")
                irdaLastInformationResponseAcked = False
                Return
            End If

            If sendIdleReceiveReady Then SendIrdaReceiveReady("RR")
        End Sub

        Private Sub HandleIrdaInformationFrame(frame As Byte())
            If frame.Length < 6 Then Return

            Dim control = frame(1)
            HandleIrdaReceiveAck(control, False)
            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence = irdaReceiveNext Then
                irdaReceiveNext = CByte((irdaReceiveNext + 1) And &H7)
                Dim dataLength = frame.Length - 4
                Dim data(dataLength - 1) As Byte
                Array.Copy(frame, 2, data, 0, dataLength)
                If HandleIrdaInformationPayload(data) Then Return
            ElseIf irdaLastInformationResponse IsNot Nothing Then
                QueueIrdaInformationRetransmit(irdaLastInformationResponseLabel)
                irdaLastInformationResponseAcked = False
                Return
            End If

            SendIrdaReceiveReady("RR")
        End Sub

        Private Function HandleIrdaInformationPayload(data As Byte()) As Boolean
            If data.Length >= 4 AndAlso (data(0) And IrdaControlBit) <> 0 AndAlso data(2) = IrdaLmpConnectCommand Then
                Dim destinationLsap = CByte(data(0) And &H7F)
                Dim sourceLsap = CByte(data(1) And &H7F)
                Dim initialCredit As Byte = If(destinationLsap = IrdaObexLsap, IrdaTinyTpCreditGrant, CByte(0))
                Dim responseLength = If(initialCredit = 0, 4, 5)
                Dim response(responseLength - 1) As Byte
                response(0) = CByte(sourceLsap Or IrdaControlBit)
                response(1) = destinationLsap
                response(2) = IrdaLmpConnectConfirm
                response(3) = 0
                If responseLength > 4 Then response(4) = initialCredit
                If destinationLsap = IrdaObexLsap Then irdaTinyTpPacketsSinceCreditGrant = 0
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
            irdaTinyTpPacketsSinceCreditGrant += 1

            Dim obexOffset = FindReceiveObexOffset(data)
            If obexOffset < 0 AndAlso irdaObexExpectedLength <= 0 Then Return False
            If obexOffset < 0 Then obexOffset = FindReceiveObexContinuationOffset(data)
            Dim cursor = obexOffset
            Dim handledAny = False

            While cursor < data.Length
                If irdaObexExpectedLength > 0 Then
                    Dim needed = irdaObexExpectedLength - irdaObexPacket.Count
                    Dim available = data.Length - cursor
                    If needed <= 0 Then
                        irdaObexExpectedLength = 0
                        irdaObexPacket.Clear()
                        Continue While
                    End If

                    Dim fragmentBytes = Math.Min(needed, available)
                    If fragmentBytes <= 0 Then Exit While

                    AppendObexFragment(data, cursor, fragmentBytes)
                    cursor += fragmentBytes
                    If irdaObexPacket.Count < irdaObexExpectedLength Then
                        LogObexFragmentProgress(obexOffset)
                        If ShouldGrantIrdaTinyTpCredit() Then
                            SendIrdaTinyTpCredit(sourceLsap)
                            handledAny = True
                        End If
                        Exit While
                    End If

                    Dim packet = irdaObexPacket.Take(irdaObexExpectedLength).ToArray()
                    irdaObexPacket.Clear()
                    irdaObexExpectedLength = 0
                    irdaObexLastFragmentLogCount = 0
                    handledAny = HandleCompleteObexPacket(packet, irdaObexClientLsap) OrElse handledAny
                    Continue While
                End If

                If data.Length - cursor < 3 Then Exit While
                Dim packetLength = (CInt(data(cursor + 1)) << 8) Or data(cursor + 2)
                If packetLength < 3 Then Exit While

                If cursor + packetLength > data.Length Then
                    irdaObexExpectedLength = packetLength
                    irdaObexClientLsap = sourceLsap
                    irdaObexFragmentOffset = cursor
                    irdaObexPacket.Clear()
                    AppendObexFragment(data, cursor, data.Length - cursor)
                    irdaObexLastFragmentLogCount = 0
                    LogObexFragmentProgress(cursor)
                    If ShouldGrantIrdaTinyTpCredit() Then
                        SendIrdaTinyTpCredit(sourceLsap)
                        handledAny = True
                    End If
                    Exit While
                End If

                Dim completePacket(packetLength - 1) As Byte
                Array.Copy(data, cursor, completePacket, 0, packetLength)
                handledAny = HandleCompleteObexPacket(completePacket, sourceLsap) OrElse handledAny
                cursor += packetLength
            End While

            Return handledAny
        End Function

        Private Shared Function FindReceiveObexOffset(data As Byte()) As Integer
            If data Is Nothing Then Return -1

            For Each offset In New Integer() {3, 2}
                If IsLikelyObexRequest(data, offset) Then Return offset
            Next

            Return -1
        End Function

        Private Function FindReceiveObexContinuationOffset(data As Byte()) As Integer
            If data Is Nothing OrElse data.Length = 0 Then Return 0
            If irdaObexFragmentOffset >= 0 AndAlso data.Length > irdaObexFragmentOffset Then Return irdaObexFragmentOffset
            Return 0
        End Function

        Private Shared Function IsLikelyObexRequest(data As Byte(), offset As Integer) As Boolean
            If data Is Nothing OrElse offset < 0 OrElse data.Length < offset + 3 Then Return False

            Select Case data(offset)
                Case &H2, &H80, &H81, &H82
                    Dim packetLength = (CInt(data(offset + 1)) << 8) Or data(offset + 2)
                    Return packetLength >= 3
                Case Else
                    Return False
            End Select
        End Function

        Private Sub AppendObexFragment(data As Byte(), offset As Integer, count As Integer)
            For i = 0 To count - 1
                irdaObexPacket.Add(data(offset + i))
            Next
        End Sub

        Private Sub LogObexFragmentProgress(offset As Integer)
            If irdaObexExpectedLength <= 0 Then Return
            If Not fullDiagnosticsCheck.Checked Then Return

            irdaObexLastFragmentLogCount = irdaObexPacket.Count
            Append($"OBEX fragment {irdaObexPacket.Count}/{irdaObexExpectedLength} offset={offset}.")
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
                    irdaObexObjectLength = -1
                    irdaObexLastProgressPercent = -1
                    irdaObexBody.Clear()
                    Dim response = New Byte() {clientLsap, IrdaObexLsap, &H10, &HA0, &H0, &H7, &H10, &H0, &H4, &H0}
                    SendIrdaInformationResponse(response, "OBEX CONNECT OK")
                    Return True
                Case &H81
                    SendIrdaObexResponse(clientLsap, &HA0, "OBEX DISCONNECT OK")
                    irdaObexObjectName = ""
                    irdaObexObjectType = ""
                    irdaObexObjectLength = -1
                    irdaObexLastProgressPercent = -1
                    irdaObexBody.Clear()
                    irdaObexPacket.Clear()
                    irdaObexExpectedLength = 0
                    irdaObexFragmentOffset = 3
                    irdaObexLastFragmentLogCount = 0
                    irdaTinyTpPacketsSinceCreditGrant = 0
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
                    AppendDiag($"OBEX opcode ${opcode:X2} ignored.")
                    Return False
            End Select
        End Function

        Private Sub SendIrdaObexResponse(clientLsap As Byte, responseCode As Byte, label As String)
            irdaTinyTpPacketsSinceCreditGrant = 0
            SendIrdaInformationResponse(New Byte() {clientLsap, IrdaObexLsap, IrdaTinyTpCreditGrant, responseCode, &H0, &H3}, label)
        End Sub

        Private Function ShouldGrantIrdaTinyTpCredit() As Boolean
            Return irdaTinyTpPacketsSinceCreditGrant >= IrdaTinyTpCreditLowWatermark
        End Function

        Private Sub SendIrdaTinyTpCredit(clientLsap As Byte)
            irdaTinyTpPacketsSinceCreditGrant = 0
            SendIrdaInformationResponse(New Byte() {clientLsap, IrdaObexLsap, IrdaTinyTpCreditGrant}, "TTP-CREDIT")
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
                            AppendDiag($"RX name: {irdaObexObjectName}")
                        ElseIf headerId = &H42 Then
                            irdaObexObjectType = DecodeObexAscii(data, cursor, valueLength)
                            AppendDiag($"RX type: {irdaObexObjectType}")
                        ElseIf headerId = &H48 OrElse headerId = &H49 Then
                            For i = 0 To valueLength - 1
                                irdaObexBody.Add(data(cursor + i))
                            Next
                            If fullDiagnosticsCheck.Checked Then Append($"RX body bytes: {irdaObexBody.Count}")
                            LogReceiveProgress(headerId = &H49)
                        End If
                        cursor += valueLength
                    Case &H80
                        cursor += 1
                    Case &HC0
                        If cursor + 4 > endOffset Then Exit While
                        If headerId = &HC3 Then
                            Dim lengthValue = ReadU32(data, cursor)
                            If lengthValue <= CUInt(Integer.MaxValue) Then irdaObexObjectLength = CInt(lengthValue)
                            irdaObexLastProgressPercent = -1
                            AppendQuiet($"Receiving {If(String.IsNullOrWhiteSpace(irdaObexObjectName), "beam", irdaObexObjectName)} ({If(irdaObexObjectLength >= 0, irdaObexObjectLength.ToString(CultureInfo.InvariantCulture), "?")} bytes).")
                        ElseIf fullDiagnosticsCheck.Checked Then
                            Append($"RX OBEX header ${headerId:X2} value=${ReadU32(data, cursor):X8}")
                        End If
                        cursor += 4
                End Select
            End While
        End Sub

        Private Sub LogReceiveProgress(finalChunk As Boolean)
            If irdaObexObjectLength <= 0 Then Return

            Dim percent = CInt(Math.Floor((irdaObexBody.Count * 100.0R) / Math.Max(1, irdaObexObjectLength)))
            Dim progressPercent = If(finalChunk OrElse irdaObexBody.Count >= irdaObexObjectLength, 100, Math.Min(100, (percent \ 5) * 5))
            If progressPercent >= 5 AndAlso progressPercent > irdaObexLastProgressPercent Then
                irdaObexLastProgressPercent = progressPercent
                AppendQuiet($"Receive {progressPercent}% ({irdaObexBody.Count}/{irdaObexObjectLength} bytes).")
            End If
        End Sub

        Private Sub SaveIrdaObexObject()
            Dim capturePath = captureBox.Text
            If String.IsNullOrWhiteSpace(capturePath) Then capturePath = Path.Combine(AppContext.BaseDirectory, "BeamCapture")
            Directory.CreateDirectory(capturePath)

            Dim fileName = If(String.IsNullOrWhiteSpace(irdaObexObjectName), $"beam_{DateTime.Now:yyyyMMdd_HHmmss}.bin", irdaObexObjectName)
            fileName = SanitizeFileName(fileName)
            fileName = AddPalmBeamExtension(fileName, irdaObexObjectType)
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
            pendingIrdaResponseLabel = $"UA ca=${connectionAddress:X2} payload={FormatBytes(payload)}"
            pendingIrdaResponseDueTick = Environment.TickCount64 + IrdaLinkSetupTurnaroundDelayMs
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
                        If length >= 1 Then negotiated(valueIndex) = PickIrdaParameterMask(negotiated(valueIndex), &H2, &H1, &H4, &H8, &H10, &H20)
                    Case &H83
                        If length >= 1 Then negotiated(valueIndex) = PickIrdaParameterMask(negotiated(valueIndex), IrdaIFieldSize64Bit, IrdaIFieldSize128Bit, IrdaIFieldSize256Bit)
                    Case &H84
                        If length >= 1 Then negotiated(valueIndex) = PickIrdaParameterMask(negotiated(valueIndex), &H1)
                End Select

                index += 2 + length
            End While

            Return negotiated
        End Function

        Private Shared Function PickIrdaParameterMask(requestedMask As Byte, ParamArray preferredMasks As Byte()) As Byte
            For Each preferred In preferredMasks
                If (requestedMask And preferred) <> 0 Then Return preferred
            Next

            For bit = 7 To 0 Step -1
                Dim mask = CByte(1 << bit)
                If (requestedMask And mask) <> 0 Then Return mask
            Next

            Return requestedMask
        End Function

        Private Shared Function FormatIrdaParameters(parameters As Byte()) As String
            If parameters Is Nothing OrElse parameters.Length = 0 Then Return "(none)"

            Dim parts As New List(Of String)
            Dim cursor = 0
            While cursor + 2 < parameters.Length
                Dim parameterId = parameters(cursor)
                Dim length = parameters(cursor + 1)
                Dim valueOffset = cursor + 2
                If valueOffset + length > parameters.Length Then
                    parts.Add($"${parameterId:X2}=truncated")
                    Exit While
                End If

                parts.Add($"${parameterId:X2}:{FormatBytes(parameters.Skip(valueOffset).Take(length))}")
                cursor += 2 + length
            End While

            If cursor < parameters.Length Then parts.Add($"tail:{FormatBytes(parameters.Skip(cursor))}")
            Return String.Join(" ", parts)
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

            Dim sendSequence = irdaTransmitNext
            Dim payload = BuildIrdaInformationPayload(data, sendSequence)
            Dim frame = BuildIrdaSirFrame(payload)
            Dim responseLabel = $"{label} ctrl=${payload(1):X2}"
            irdaTransmitNext = CByte((irdaTransmitNext + 1) And &H7)
            irdaLastInformationResponse = frame
            irdaLastInformationResponseData = CType(data.Clone(), Byte())
            irdaLastInformationResponseSendSequence = sendSequence
            irdaLastInformationResponseLabel = responseLabel
            irdaLastInformationResponseAcked = False
            QueueIrdaResponse(frame, responseLabel, IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Sub QueueIrdaInformationRetransmit(label As String)
            If irdaLastInformationResponseData Is Nothing Then Return

            Dim payload = BuildIrdaInformationPayload(irdaLastInformationResponseData, irdaLastInformationResponseSendSequence)
            irdaLastInformationResponse = BuildIrdaSirFrame(payload)
            QueueIrdaResponse(irdaLastInformationResponse, $"{label} ctrl=${payload(1):X2}", IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Function BuildIrdaInformationPayload(data As Byte(), sendSequence As Byte) As Byte()
            Dim safeData = If(data, Array.Empty(Of Byte)())
            Dim control = CByte(IrdaFinalBit Or ((sendSequence And &H7) << 1) Or ((irdaReceiveNext And &H7) << 5))
            Dim payload(2 + safeData.Length - 1) As Byte
            payload(0) = irdaConnectionAddress
            payload(1) = control
            Array.Copy(safeData, 0, payload, 2, safeData.Length)
            Return payload
        End Function

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
            irdaBeamLastBodyProgressPercent = -1
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
            If frame.Length < 2 Then Return False
            If fullDiagnosticsCheck.Checked OrElse irdaBeamState <> IrdaBeamSendState.ObexPutBody Then
                AppendDiag($"RX beam state={irdaBeamState} addr=${frame(0):X2} ctrl=${frame(1):X2} len={frame.Length}.")
            End If

            If irdaBeamState = IrdaBeamSendState.Discover AndAlso
                frame.Length >= 13 AndAlso frame(0) = IrdaDiscoveryResponseAddress AndAlso frame(1) = IrdaXidResponseControl Then
                irdaBeamPeerAddress = ReadU32(frame, 3)
                irdaBeamLastProgressTick = Environment.TickCount64
                AppendDiag($"Beam peer found: ${irdaBeamPeerAddress:X8}.")
                irdaBeamState = IrdaBeamSendState.Snrm
                SendIrdaBeamSnrm()
                Return True
            End If

            If irdaBeamState = IrdaBeamSendState.Snrm AndAlso frame.Length >= 2 AndAlso frame(1) = IrdaUaResponseControl Then
                ConfigureIrdaBeamBodyChunkSize(frame)
                irdaBeamReceiveNext = 0
                irdaBeamTransmitNext = 0
                irdaBeamLastProgressTick = Environment.TickCount64
                AppendDiag($"Beam link established. Body chunk {irdaBeamBodyChunkSize} bytes.")
                irdaBeamState = IrdaBeamSendState.IasConnect
                SendIrdaBeamInformation(BuildLmpConnectPayload(0, 1, 0), "IAS-CONNECT")
                Return True
            End If

            If frame(0) <> irdaBeamConnectionAddress AndAlso frame(0) <> (irdaBeamConnectionAddress Or 1) Then
                AppendDiag($"Beam ignored frame state={irdaBeamState}: {FormatBytes(frame.Take(Math.Min(frame.Length, 24)))}")
                Return False
            End If
            Dim control = frame(1)
            If IsIrdaReceiveReadyControl(control) Then
                irdaBeamLastProgressTick = Environment.TickCount64
                Dim receiveAck = CByte((control >> 5) And &H7)
                If receiveAck = irdaBeamTransmitNext Then
                    irdaBeamLastInformationFrame = Nothing
                    irdaBeamLastInformationLabel = ""
                ElseIf irdaBeamLastInformationFrame IsNot Nothing Then
                    WriteSerial(irdaBeamLastInformationFrame, $"{irdaBeamLastInformationLabel} RETX")
                End If
                Return True
            End If
            If control = IrdaDiscControl Then
                SendIrdaBeamControl(IrdaUaResponseControl, "BEAM-UA-DISC")
                ResetIrdaBeamSender()
                Return True
            End If
            If Not IsIrdaInformationControl(control) OrElse frame.Length < 6 Then Return True

            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence <> irdaBeamReceiveNext Then
                SendIrdaBeamReceiveReady("BEAM-RR-DUP")
                Return True
            End If

            irdaBeamReceiveNext = CByte((irdaBeamReceiveNext + 1) And &H7)
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
                        AppendDiag("Beam IAS connected; querying OBEX LSAP.")
                        irdaBeamState = IrdaBeamSendState.IasQuery
                        irdaBeamIasQueryIndex = 0
                        SendNextIrdaBeamIasQuery()
                    Else
                        AppendDiag($"Beam unexpected IAS response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
                Case IrdaBeamSendState.IasQuery
                    Dim lsap As Byte = 0
                    If TryParseIasLsapResponse(data, lsap) Then
                        irdaBeamRemoteObexLsap = lsap
                        AppendDiag($"Beam IAS returned OBEX LSAP=${irdaBeamRemoteObexLsap:X2}; connecting OBEX.")
                        irdaBeamState = IrdaBeamSendState.ObexLmpConnect
                        SendIrdaBeamInformation(BuildLmpConnectPayload(irdaBeamRemoteObexLsap, 3, &H10), "OBEX-LMP-CONNECT")
                    ElseIf data.Length >= 4 AndAlso (data(2) And &H7F) = IrdaIasGetValueByClass Then
                        AppendDiag($"Beam IAS query rejected status=${data(3):X2}: {FormatBytes(data.Take(Math.Min(data.Length, 24)))}")
                        If Not SendNextIrdaBeamIasQuery() Then AppendDiag("Beam IAS query exhausted.")
                    Else
                        AppendDiag($"Beam unexpected IAS query response: {FormatBytes(data.Take(Math.Min(data.Length, 24)))}")
                    End If
                Case IrdaBeamSendState.ObexLmpConnect
                    If data.Length >= 4 AndAlso data(2) = IrdaLmpConnectConfirm Then
                        AppendDiag("Beam OBEX link connected.")
                        irdaBeamState = IrdaBeamSendState.ObexConnect
                        SendIrdaBeamInformation(WrapTinyTpObex(BuildObexConnectPacket()), "OBEX-CONNECT")
                    Else
                        AppendDiag($"Beam unexpected OBEX link response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
                Case IrdaBeamSendState.ObexConnect
                    If IsObexResponse(data, &HA0) Then
                        AppendDiag("Beam OBEX connected; sending metadata.")
                        irdaBeamState = IrdaBeamSendState.ObexPutMeta
                        SendIrdaBeamPutMetadata()
                    Else
                        AppendDiag($"Beam unexpected OBEX connect response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
                Case IrdaBeamSendState.ObexPutMeta
                    If IsObexResponse(data, &H90) OrElse IsObexResponse(data, &HA0) Then
                        AppendDiag("Beam metadata accepted; sending file body.")
                        irdaBeamState = IrdaBeamSendState.ObexPutBody
                        SendIrdaBeamNextBodyChunk()
                    Else
                        AppendDiag($"Beam unexpected metadata response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
                Case IrdaBeamSendState.ObexPutBody
                    If IsObexResponse(data, &H90) Then
                        SendIrdaBeamNextBodyChunk()
                    ElseIf IsObexResponse(data, &HA0) Then
                        AppendDiag("Beam body accepted; disconnecting.")
                        irdaBeamState = IrdaBeamSendState.ObexDisconnect
                        SendIrdaBeamInformation(WrapTinyTpObex(New Byte() {&H81, &H0, &H3}), "OBEX-DISCONNECT")
                    ElseIf fullDiagnosticsCheck.Checked Then
                        AppendDiag($"Beam unexpected body response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
                Case IrdaBeamSendState.ObexDisconnect
                    If IsObexResponse(data, &HA0) Then
                        AppendDiag("Beam OBEX disconnected.")
                        irdaBeamState = IrdaBeamSendState.LinkDisconnect
                        SendIrdaBeamControl(IrdaDiscControl, "BEAM-DISC")
                    Else
                        AppendDiag($"Beam unexpected disconnect response: {FormatBytes(data.Take(Math.Min(data.Length, 16)))}")
                    End If
            End Select
        End Sub

        Private Sub SendIrdaBeamDiscovery()
            Dim slot = irdaBeamDiscoverySlot
            If slot > 5 Then slot = -1
            irdaBeamDiscoverySlot = slot + 1

            Dim payload(13) As Byte
            payload(0) = IrdaBroadcastAddress
            payload(1) = IrdaUiControl
            payload(2) = IrdaXidFormat
            WriteU32(payload, 3, IrdaHostAddress)
            WriteU32(payload, 7, &HFFFFFFFFUI)
            payload(11) = 0
            payload(12) = If(slot < 0, CByte(&HFF), CByte(slot))
            payload(13) = 0
            SendIrdaBeamPayload(payload, $"BEAM-XID slot=${payload(12):X2}")
        End Sub

        Private Sub SendIrdaBeamSnrm()
            Dim parameters = New Byte() {
                &H1, &H1, &H2,
                &H82, &H1, &H1,
                &H83, &H1, IrdaBeamOfferedIFieldSizeMask,
                &H84, &H1, &H1,
                &H85, &H1, &H8,
                &H86, &H1, &H7,
                &H8, &H1, &HFF
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
            Dim frame = BuildIrdaSirFrame(payload)
            irdaBeamLastInformationFrame = frame
            irdaBeamLastInformationLabel = $"{label} ctrl=${control:X2}"
            irdaBeamTransmitNext = CByte((irdaBeamTransmitNext + 1) And &H7)
            WriteSerial(frame, irdaBeamLastInformationLabel)
            irdaBeamLastProgressTick = Environment.TickCount64
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

        Private Sub SendIrdaBeamReceiveReady(label As String)
            Dim control = CByte(IrdaRrPollFinalControl Or ((irdaBeamReceiveNext And &H7) << 5))
            SendIrdaBeamControl(control, label)
        End Sub

        Private Sub SendIrdaBeamPutMetadata()
            Dim headers As New List(Of Byte)
            headers.AddRange(BuildObexNameHeader(irdaBeamFileName))
            Dim objectType = IrdaBeamObexTypeForFile(irdaBeamFileName)
            If objectType.Length > 0 Then headers.AddRange(BuildObexTypeHeader(objectType))
            headers.AddRange(BuildObexLengthHeader(If(irdaBeamFileBytes Is Nothing, 0, irdaBeamFileBytes.Length)))
            If IsIrdaBeamTextFile() Then
                headers.AddRange(BuildObexUnicodeHeader(&H5, Path.GetFileNameWithoutExtension(irdaBeamFileName)))
                headers.AddRange(BuildObexU32Header(&HCF, &H6D656D6FUI)) ' "memo" Exchange Manager target.
            End If

            Dim packet = BuildObexPacket(&H2, headers.ToArray())
            SendIrdaBeamInformation(WrapTinyTpObex(packet), If(objectType.Length = 0, "OBEX-PUT-META", $"OBEX-PUT-META type={objectType}"))
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

            Dim chunk = Math.Min(irdaBeamBodyChunkSize, remaining)
            Dim body(chunk - 1) As Byte
            Array.Copy(irdaBeamFileBytes, irdaBeamOffset, body, 0, chunk)
            irdaBeamOffset += chunk
            Dim finalChunk = irdaBeamOffset >= irdaBeamFileBytes.Length
            Dim opcode As Byte = If(finalChunk, CByte(&H82), CByte(&H2))
            Dim headerId As Byte = If(finalChunk, CByte(&H49), CByte(&H48))
            SendIrdaBeamInformation(WrapTinyTpObex(BuildObexPacket(opcode, BuildObexBodyHeader(headerId, body))), BuildIrdaBeamBodyProgressLabel(finalChunk))
        End Sub

        Private Function BuildIrdaBeamBodyProgressLabel(finalChunk As Boolean) As String
            If irdaBeamFileBytes Is Nothing OrElse irdaBeamFileBytes.Length = 0 Then Return "OBEX-PUT-BODY 100%"

            Dim totalPackets = CInt(Math.Ceiling(irdaBeamFileBytes.Length / CDbl(irdaBeamBodyChunkSize)))
            Dim sentPackets = CInt(Math.Ceiling(irdaBeamOffset / CDbl(irdaBeamBodyChunkSize)))
            Dim percent = CInt(Math.Floor((sentPackets * 100.0R) / Math.Max(1, totalPackets)))
            Dim progressPercent = If(finalChunk, 100, Math.Min(100, (percent \ 5) * 5))

            If progressPercent >= 5 AndAlso progressPercent > irdaBeamLastBodyProgressPercent Then
                irdaBeamLastBodyProgressPercent = progressPercent
                Return $"OBEX-PUT-BODY {progressPercent}% ({sentPackets}/{totalPackets} packets, {irdaBeamOffset}/{irdaBeamFileBytes.Length} bytes)"
            End If

            Return "OBEX-PUT-BODY"
        End Function

        Private Sub ResetLinkState()
            SyncLock serialLock
                rxBytes.Clear()
            End SyncLock
            irdaFrameBuffer.Clear()
            irdaInFrame = False
            irdaEscaped = False
            irdaRespondedInDiscovery = False
            irdaLastDiscoverySource = 0
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
            irdaLastInformationResponseData = Nothing
            irdaLastInformationResponseSendSequence = 0
            irdaLastInformationResponseLabel = ""
            irdaLastInformationResponseAcked = False
            irdaObexObjectName = ""
            irdaObexObjectType = ""
            irdaObexObjectLength = -1
            irdaObexLastProgressPercent = -1
            irdaObexBody.Clear()
            irdaObexPacket.Clear()
            irdaObexExpectedLength = 0
            irdaObexClientLsap = 0
            irdaObexFragmentOffset = 3
            irdaObexLastFragmentLogCount = 0
            irdaTinyTpPacketsSinceCreditGrant = 0
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
            irdaBeamRemoteObexLsap = IrdaObexLsap
            irdaBeamIasQueryIndex = 0
            irdaBeamLastBodyProgressPercent = -1
            irdaBeamBodyChunkSize = IrdaBeamDefaultBodyChunkSize
            irdaBeamLastInformationFrame = Nothing
            irdaBeamLastInformationLabel = ""
        End Sub

        Private Sub ConfigureIrdaBeamBodyChunkSize(uaFrame As Byte())
            irdaBeamBodyChunkSize = IrdaBeamDefaultBodyChunkSize
            If uaFrame Is Nothing OrElse uaFrame.Length <= 12 Then Return

            Dim parametersLength = uaFrame.Length - 12
            Dim selectedIFieldSize = ParseNegotiatedIFieldSize(uaFrame, 10, parametersLength)
            If selectedIFieldSize <= 0 Then Return

            Dim bodyChunkSize = Math.Max(IrdaBeamDefaultBodyChunkSize, selectedIFieldSize - IrdaBeamBodyPacketOverhead)
            irdaBeamBodyChunkSize = Math.Min(IrdaBeamMaxBodyChunkSize, bodyChunkSize)
        End Sub

        Private Shared Function ParseNegotiatedIFieldSize(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim endOffset = Math.Min(buffer.Length - 2, offset + count)
            Dim cursor = offset
            While cursor + 2 < endOffset
                Dim parameterId = buffer(cursor)
                Dim length = buffer(cursor + 1)
                Dim valueOffset = cursor + 2
                If valueOffset + length > endOffset Then Exit While

                If parameterId = &H83 AndAlso length >= 1 Then
                    Return IFieldSizeFromMask(buffer(valueOffset))
                End If

                cursor += 2 + length
            End While

            Return 0
        End Function

        Private Shared Function IFieldSizeFromMask(mask As Byte) As Integer
            If (mask And IrdaIFieldSize256Bit) <> 0 Then Return 256
            If (mask And IrdaIFieldSize128Bit) <> 0 Then Return 128
            If (mask And IrdaIFieldSize64Bit) <> 0 Then Return 64
            Return 0
        End Function

        Private Sub WriteSerial(bytes As Byte(), label As String)
            If serial Is Nothing OrElse Not serial.IsOpen OrElse bytes Is Nothing OrElse bytes.Length = 0 Then Return
            Try
                serial.Write(bytes, 0, bytes.Length)
                If ShouldLogSerialLabel(label) Then
                    If fullDiagnosticsCheck.Checked Then
                        Append($"TX {label} ({bytes.Length} bytes).")
                    Else
                        Append(FormatQuietSerialLabel(label))
                    End If
                End If
            Catch ex As Exception
                Append($"TX failed: {ex.Message}")
            End Try
        End Sub

        Private Function ShouldLogSerialLabel(label As String) As Boolean
            If label.StartsWith("BEAM-XID", StringComparison.Ordinal) Then Return False
            If fullDiagnosticsCheck.Checked Then Return True

            Return label.StartsWith("OBEX-PUT-BODY ", StringComparison.Ordinal) AndAlso
                label.IndexOf("%", StringComparison.Ordinal) >= 0
        End Function

        Private Shared Function FormatQuietSerialLabel(label As String) As String
            Const prefix = "OBEX-PUT-BODY "
            If Not label.StartsWith(prefix, StringComparison.Ordinal) Then Return label

            Dim percentEnd = label.IndexOf("%", StringComparison.Ordinal)
            If percentEnd < 0 Then Return "Send progress."

            Dim percentText = label.Substring(prefix.Length, percentEnd - prefix.Length + 1)
            Dim bytesMarker = " bytes"
            Dim bytesEnd = label.IndexOf(bytesMarker, StringComparison.Ordinal)
            If bytesEnd < 0 Then Return $"Send {percentText}."

            Dim commaBeforeBytes = label.LastIndexOf(", ", bytesEnd, StringComparison.Ordinal)
            If commaBeforeBytes < 0 Then Return $"Send {percentText}."

            Dim bytesText = label.Substring(commaBeforeBytes + 2, bytesEnd - commaBeforeBytes - 2)
            Return $"Send {percentText} ({bytesText} bytes)."
        End Function

        Private Shared Function BuildLmpConnectPayload(destinationLsap As Byte, sourceLsap As Byte, initialCredit As Byte) As Byte()
            If initialCredit = 0 Then
                Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0}
            End If
            Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0, initialCredit}
        End Function

        Private Function SendNextIrdaBeamIasQuery() As Boolean
            If irdaBeamIasQueryIndex >= IrdaObexIasClasses.Length Then Return False

            Dim className = IrdaObexIasClasses(irdaBeamIasQueryIndex)
            irdaBeamIasQueryIndex += 1
            SendIrdaBeamInformation(BuildIasObexQueryPayload(className), $"IAS-OBEX-QUERY {className}")
            Return True
        End Function

        Private Shared Function BuildIasObexQueryPayload(classNameText As String) As Byte()
            Dim className = Encoding.ASCII.GetBytes(If(classNameText, "OBEX"))
            Dim attributeName = Encoding.ASCII.GetBytes("IrDA:TinyTP:LsapSel")
            Dim payload As New List(Of Byte) From {&H0, &H1, CByte(IrdaIasGetValueByClass Or IrdaIasLast)}
            payload.Add(CByte(className.Length))
            payload.AddRange(className)
            payload.Add(CByte(attributeName.Length))
            payload.AddRange(attributeName)
            Return payload.ToArray()
        End Function

        Private Shared Function TryParseIasLsapResponse(data As Byte(), ByRef lsap As Byte) As Boolean
            If data Is Nothing OrElse data.Length < 13 Then Return False
            If (data(0) And &H7F) <> 1 OrElse (data(1) And &H7F) <> 0 Then Return False
            If (data(2) And &H7F) <> IrdaIasGetValueByClass OrElse data(3) <> IrdaIasSuccess Then Return False

            Dim cursor = 4
            Dim objectCount = (CInt(data(cursor)) << 8) Or data(cursor + 1)
            cursor += 2
            If objectCount <= 0 Then Return False

            For objectIndex = 0 To objectCount - 1
                If cursor + 3 > data.Length Then Return False
                cursor += 2 ' object id

                Dim valueType = data(cursor)
                cursor += 1
                Select Case valueType
                    Case IrdaIasInteger
                        If cursor + 4 > data.Length Then Return False
                        Dim value = ReadU32(data, cursor)
                        cursor += 4
                        If value <= &H7FUI Then
                            lsap = CByte(value)
                            Return True
                        End If
                    Case Else
                        Return False
                End Select
            Next

            Return False
        End Function

        Private Function WrapTinyTpObex(obexPacket As Byte()) As Byte()
            Dim payload(3 + obexPacket.Length - 1) As Byte
            payload(0) = irdaBeamRemoteObexLsap
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

        Private Shared Function BuildObexTypeHeader(mimeType As String) As Byte()
            Dim typeBytes = Encoding.ASCII.GetBytes(If(mimeType, ""))
            Dim header(3 + typeBytes.Length) As Byte
            header(0) = &H42
            WriteU16(header, 1, CUShort(header.Length))
            Array.Copy(typeBytes, 0, header, 3, typeBytes.Length)
            header(header.Length - 1) = 0
            Return header
        End Function

        Private Shared Function BuildObexBodyHeader(headerId As Byte, bytes As Byte()) As Byte()
            Return BuildObexVariableHeader(headerId, If(bytes, Array.Empty(Of Byte)()))
        End Function

        Private Shared Function BuildObexU32Header(headerId As Byte, value As UInteger) As Byte()
            Dim header(4) As Byte
            header(0) = headerId
            WriteU32(header, 1, value)
            Return header
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

        Private Function IsObexResponse(data As Byte(), responseCode As Byte) As Boolean
            Dim cursor = FindIrdaBeamObexOffset(data, responseCode)
            Return cursor >= 0
        End Function

        Private Function FindIrdaBeamObexOffset(data As Byte(), responseCode As Byte) As Integer
            If data Is Nothing Then Return -1

            For Each offset In New Integer() {3, 2, 0}
                If data.Length < offset + 3 OrElse data(offset) <> responseCode Then Continue For

                Dim packetLength = (CInt(data(offset + 1)) << 8) Or data(offset + 2)
                If packetLength >= 3 AndAlso offset + packetLength <= data.Length Then Return offset
            Next

            Return -1
        End Function

        Private Function IsIrdaBeamTextFile() As Boolean
            Return Path.GetExtension(If(irdaBeamFileName, "")).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function IrdaBeamObexTypeForFile(fileName As String) As String
            Select Case Path.GetExtension(If(fileName, "")).ToLowerInvariant()
                Case ".vcf"
                    Return "text/x-vCard"
                Case ".vcs"
                    Return "text/x-vCalendar"
                Case ".txt"
                    Return "text/plain"
                Case Else
                    Return ""
            End Select
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

        Private Shared Function IsSupportedIrdaObexIasQuery(className As String, attributeName As String) As Boolean
            If Not String.Equals(attributeName, "IrDA:TinyTP:LsapSel", StringComparison.OrdinalIgnoreCase) Then Return False
            Return IrdaObexIasClasses.Any(Function(name) String.Equals(name, className, StringComparison.OrdinalIgnoreCase))
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
            If Not IsSupportedIrdaObexIasQuery(className, attributeName) Then
                Dim emptyResponse = New Byte() {clientLsap, 0, CByte(IrdaIasGetValueByClass Or IrdaIasLast), IrdaIasSuccess, 0, 0}
                SendIrdaInformationResponse(emptyResponse, $"IAS {className}/{attributeName} no match")
                Return
            End If

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

        Private Shared Function AddPalmBeamExtension(fileName As String, objectType As String) As String
            If Not String.IsNullOrWhiteSpace(Path.GetExtension(fileName)) Then Return fileName

            Select Case If(objectType, "").Trim().ToLowerInvariant()
                Case "text/x-vcard", "text/vcard"
                    Return fileName & ".vcf"
                Case "text/x-vcalendar", "text/calendar"
                    Return fileName & ".vcs"
                Case "text/plain"
                    Return fileName & ".txt"
                Case Else
                    Return fileName
            End Select
        End Function

        Private Shared Function FormatBytes(bytes As IEnumerable(Of Byte)) As String
            If bytes Is Nothing Then Return ""
            Return String.Join(" ", bytes.Select(Function(value) value.ToString("X2", CultureInfo.InvariantCulture)))
        End Function

        Private Sub AppendDiag(message As String)
            If fullDiagnosticsCheck.Checked Then Append(message)
        End Sub

        Private Sub AppendQuiet(message As String)
            Append(message)
        End Sub

        Private Sub Append(message As String)
            Dim line = $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"
            traceBox.AppendText(line)
        End Sub
    End Class
End Namespace
