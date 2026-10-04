Option Explicit

Dim WshShell, FSO, scriptDir, ipFile, defaultIp, promptMsg, ipInput, target
Dim execObj, output, connectedWifi, fileObj, ipOnly, colonPos, avgPing, userChoice
Dim msgSlow, scrcpyCmd, adbPath, scrcpyPath, tryTarget, baseIp

Set WshShell = CreateObject("WScript.Shell")
Set FSO = CreateObject("Scripting.FileSystemObject")

' Garante que o diretorio de trabalho e o do proprio executavel SCRCPY
scriptDir = FSO.GetParentFolderName(WScript.ScriptFullName)
WshShell.CurrentDirectory = scriptDir

adbPath = scriptDir & "\adb.exe"
scrcpyPath = scriptDir & "\scrcpy.exe"
ipFile = scriptDir & "\last_ip.txt"

' Encerra qualquer instancia anterior orfa para evitar conflito de banda
On Error Resume Next
WshShell.Run "taskkill /f /im scrcpy.exe", 0, True
On Error GoTo 0

' Recupera o ultimo IP utilizado
defaultIp = "192.168.1.100:5555"
If FSO.FileExists(ipFile) Then
    On Error Resume Next
    Set fileObj = FSO.OpenTextFile(ipFile, 1)
    If Not fileObj.AtEndOfStream Then
        Dim savedIp
        savedIp = Trim(fileObj.ReadLine())
        If savedIp <> "" And InStr(savedIp, "192.168.100.") = 0 Then
            defaultIp = savedIp
        End If
    End If
    fileObj.Close
    On Error GoTo 0
End If

' Tenta autodetectar aparelho Wi-Fi via mDNS
Dim detectedMdns, mdnsLines, mLine, regEx, matches, candIp, anyPort
detectedMdns = ""
anyPort = ""
On Error Resume Next
Set execObj = WshShell.Exec("""" & adbPath & """ mdns services")
Do While execObj.Status = 0
    WScript.Sleep 50
Loop
output = execObj.StdOut.ReadAll()
mdnsLines = Split(output, vbCrLf)
Set regEx = New RegExp
regEx.Pattern = "\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{2,5})\b"
For Each mLine In mdnsLines
    If InStr(mLine, "_adb-tls-connect") > 0 Or InStr(mLine, "_adb._tcp") > 0 Then
        Set matches = regEx.Execute(mLine)
        If matches.Count > 0 Then
            candIp = matches(0).SubMatches(0)
            anyPort = matches(0).SubMatches(1)
            ' Prioriza IP da sub-rede local 192.168.x e ignora interfaces virtuais RNDIS 10.131.x
            If InStr(candIp, "192.168.") = 1 Then
                detectedMdns = matches(0).Value
                Exit For
            ElseIf detectedMdns = "" And InStr(candIp, "10.131.") = 0 Then
                detectedMdns = matches(0).Value
            End If
        End If
    End If
Next

' Se encontrou porta mDNS mas o IP era virtual, associa a sub-rede real
If detectedMdns = "" And anyPort <> "" Then
    detectedMdns = "192.168.1.100:" & anyPort
End If
On Error GoTo 0

If detectedMdns <> "" Then
    defaultIp = detectedMdns
End If

promptMsg = "Aura - Espelhamento SCRCPY (Otimizado & Fluido)" & vbCrLf & vbCrLf & _
            "Digite o IP e a Porta do seu celular na rede Wi-Fi:" & vbCrLf & _
            "- Detectado na rede (Wi-Fi): " & defaultIp & vbCrLf & _
            "- Padrao classico TCP/IP: 192.168.1.100:5555" & vbCrLf & _
            "- Se omitir a porta (ex: 192.168.1.100), tentara a porta 5555 e a porta dinamica." & vbCrLf & vbCrLf & _
            "Pressione [Cancelar] ou deixe em branco para conectar direto via cabo USB."

ipInput = InputBox(promptMsg, "Aura SCRCPY - Conexao Wi-Fi", defaultIp)

target = Trim(ipInput)

' Se deixou em branco ou cancelou, vai direto para o USB
If target = "" Then
    ConnectViaUsb "Iniciado diretamente em modo USB."
    WScript.Quit
End If

' Formata porta e tenta conexao ADB inteligente
connectedWifi = False

If InStr(target, ":") = 0 Then
    ' Tenta 1: porta 5555
    tryTarget = target & ":5555"
    Set execObj = WshShell.Exec("""" & adbPath & """ connect " & tryTarget)
    Do While execObj.Status = 0
        WScript.Sleep 100
    Loop
    output = execObj.StdOut.ReadAll()
    If (InStr(LCase(output), "connected to " & LCase(tryTarget)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
        target = tryTarget
        connectedWifi = True
    ElseIf anyPort <> "" Then
        ' Tenta 2: porta dinamica mDNS
        tryTarget = target & ":" & anyPort
        Set execObj = WshShell.Exec("""" & adbPath & """ connect " & tryTarget)
        Do While execObj.Status = 0
            WScript.Sleep 100
        Loop
        output = execObj.StdOut.ReadAll()
        If (InStr(LCase(output), "connected to " & LCase(tryTarget)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
            target = tryTarget
            connectedWifi = True
        End If
    End If
    
    ' Tenta 3: se USB fisico estiver conectado, ativa tcpip 5555
    If Not connectedWifi Then
        Set execObj = WshShell.Exec("""" & adbPath & """ devices")
        Do While execObj.Status = 0
            WScript.Sleep 100
        Loop
        output = execObj.StdOut.ReadAll()
        If CheckAuthorizedUsb(output) Then
            WshShell.Run """" & adbPath & """ -d tcpip 5555", 0, True
            WScript.Sleep 500
            tryTarget = target & ":5555"
            Set execObj = WshShell.Exec("""" & adbPath & """ connect " & tryTarget)
            Do While execObj.Status = 0
                WScript.Sleep 100
            Loop
            output = execObj.StdOut.ReadAll()
            If (InStr(LCase(output), "connected to " & LCase(tryTarget)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
                target = tryTarget
                connectedWifi = True
            End If
        End If
    End If
    
    If Not connectedWifi Then target = target & ":5555"
Else
    Set execObj = WshShell.Exec("""" & adbPath & """ connect " & target)
    Do While execObj.Status = 0
        WScript.Sleep 100
    Loop
    output = execObj.StdOut.ReadAll()
    If (InStr(LCase(output), "connected to " & LCase(target)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
        connectedWifi = True
    Else
        colonPos = InStr(target, ":")
        baseIp = Left(target, colonPos - 1)
        
        ' Fallback 1: se porta falhou e temos porta dinamica mDNS
        If anyPort <> "" And target <> (baseIp & ":" & anyPort) Then
            tryTarget = baseIp & ":" & anyPort
            Set execObj = WshShell.Exec("""" & adbPath & """ connect " & tryTarget)
            Do While execObj.Status = 0
                WScript.Sleep 100
            Loop
            output = execObj.StdOut.ReadAll()
            If (InStr(LCase(output), "connected to " & LCase(tryTarget)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
                target = tryTarget
                connectedWifi = True
            End If
        End If
        
        ' Fallback 2: se USB fisico estiver conectado, ativa tcpip 5555
        If Not connectedWifi Then
            Set execObj = WshShell.Exec("""" & adbPath & """ devices")
            Do While execObj.Status = 0
                WScript.Sleep 100
            Loop
            output = execObj.StdOut.ReadAll()
            If CheckAuthorizedUsb(output) Then
                WshShell.Run """" & adbPath & """ -d tcpip 5555", 0, True
                WScript.Sleep 500
                tryTarget = baseIp & ":5555"
                Set execObj = WshShell.Exec("""" & adbPath & """ connect " & tryTarget)
                Do While execObj.Status = 0
                    WScript.Sleep 100
                Loop
                output = execObj.StdOut.ReadAll()
                If (InStr(LCase(output), "connected to " & LCase(tryTarget)) > 0 Or InStr(LCase(output), "already connected") > 0) And InStr(LCase(output), "cannot") = 0 And InStr(LCase(output), "unable") = 0 And InStr(LCase(output), "failed") = 0 Then
                    target = tryTarget
                    connectedWifi = True
                End If
            End If
        End If
    End If
End If

If Not connectedWifi Then
    ConnectViaUsb "Nao foi possivel conectar ao Wi-Fi (" & target & ")."
    WScript.Quit
End If

' Memoriza o IP de sucesso no arquivo
On Error Resume Next
Set fileObj = FSO.CreateTextFile(ipFile, True)
fileObj.WriteLine target
fileObj.Close
On Error GoTo 0

' Extrai apenas o endereco IP (sem porta) para medir a latencia
ipOnly = target
colonPos = InStr(target, ":")
If colonPos > 0 Then
    ipOnly = Left(target, colonPos - 1)
End If

' 2. Medicao de Latencia do Wi-Fi (Smart Probe)
avgPing = MeasureWifiPing(ipOnly)

' Se a latencia for alta (> 50ms), aciona o fallback inteligente
If avgPing > 50 Then
    msgSlow = "Alta Latencia Detectada no Wi-Fi!" & vbCrLf & vbCrLf & _
              "- Latencia media da sua rede: " & avgPing & " ms" & vbCrLf & _
              "- Recomendado para espelhamento: menos de 20 ms" & vbCrLf & _
              "- No Wi-Fi atual, voce tera atraso (delay) perceptivel nos toques." & vbCrLf & vbCrLf & _
              "Deseja alternar para conexao via CABO USB para fluidez perfeita a 90 FPS e ZERO lag?" & vbCrLf & vbCrLf & _
              "- [Sim] = Conectar cabo USB (Recomendado - 0 Delay)" & vbCrLf & _
              "- [Nao] = Continuar no Wi-Fi mesmo com atraso"
              
    userChoice = MsgBox(msgSlow, vbExclamation + vbYesNo + vbDefaultButton1, "Aura SCRCPY - Alerta de Latencia Wi-Fi")
    If userChoice = vbYes Then
        ConnectViaUsb "Alternado pelo usuario devido a alta latencia do Wi-Fi (" & avgPing & " ms)."
        WScript.Quit
    End If
End If

' 3. Inicia via Wi-Fi
LaunchScrcpyWifi target
WScript.Quit

' ==============================================================================
' FUNCAO: LANCA ESPELHAMENTO VIA WI-FI
' ==============================================================================
Sub LaunchScrcpyWifi(wifiTarget)
    WshShell.Run """" & adbPath & """ -s " & wifiTarget & " shell input keyevent 224", 0, True
    WshShell.Run """" & adbPath & """ -s " & wifiTarget & " shell wm dismiss-keyguard", 0, True
    WshShell.Run """" & adbPath & """ -s " & wifiTarget & " shell svc power stayon true", 0, True

    scrcpyCmd = """" & scrcpyPath & """ -s " & wifiTarget & _
                " -S" & _
                " --stay-awake" & _
                " --video-codec=h265" & _
                " --video-buffer=50" & _
                " --video-bit-rate=10M" & _
                " --max-size=1600" & _
                " --max-fps=90" & _
                " --no-audio" & _
                " --disable-screensaver" & _
                " --shortcut-mod=lalt,lctrl" & _
                " --window-title=""Aura - Wi-Fi (Fluido)"""

    WshShell.Run scrcpyCmd, 0, False
End Sub

' ==============================================================================
' FUNCAO: MEDICAO RAPIDA DE PING DO WI-FI
' ==============================================================================
Function MeasureWifiPing(ipAddress)
    Dim execPing, pingOutput, pingLines, pLine, sumMs, countMs, pos, posMs, valStr
    sumMs = 0
    countMs = 0
    
    Set execPing = WshShell.Exec("ping.exe -n 2 " & ipAddress)
    Do While execPing.Status = 0
        WScript.Sleep 50
    Loop
    pingOutput = execPing.StdOut.ReadAll()
    
    pingLines = Split(pingOutput, vbCrLf)
    For Each pLine In pingLines
        pLine = LCase(pLine)
        pos = InStr(pLine, "tempo=")
        If pos = 0 Then pos = InStr(pLine, "tempo<")
        If pos = 0 Then pos = InStr(pLine, "time=")
        If pos = 0 Then pos = InStr(pLine, "time<")
        
        If pos > 0 Then
            posMs = InStr(pos, pLine, "ms")
            If posMs > pos Then
                valStr = Trim(Mid(pLine, pos + 6, posMs - (pos + 6)))
                If IsNumeric(valStr) Then
                    sumMs = sumMs + CLng(valStr)
                    countMs = countMs + 1
                ElseIf InStr(pLine, "<1ms") > 0 Then
                    sumMs = sumMs + 1
                    countMs = countMs + 1
                End If
            End If
        End If
    Next
    
    If countMs > 0 Then
        MeasureWifiPing = Round(sumMs / countMs)
    Else
        MeasureWifiPing = 999
    End If
End Function

' ==============================================================================
' FUNCAO: FALLBACK INTELIGENTE PARA CABO USB
' ==============================================================================
Sub ConnectViaUsb(reasonMsg)
    Dim execDev, devOutput, hasUsb, promptUsb, userResp
    
    Do
        Set execDev = WshShell.Exec("""" & adbPath & """ devices")
        Do While execDev.Status = 0
            WScript.Sleep 100
        Loop
        devOutput = execDev.StdOut.ReadAll()
        
        hasUsb = CheckAuthorizedUsb(devOutput)
        If hasUsb Then Exit Do
        
        promptUsb = reasonMsg & vbCrLf & vbCrLf & _
                    "Por favor, conecte o cabo USB para continuar:" & vbCrLf & vbCrLf & _
                    "1. Conecte o cabo USB no PC e no celular." & vbCrLf & _
                    "2. Confirme a permissao de Depuracao USB na tela do aparelho." & vbCrLf & vbCrLf & _
                    "Clique em [OK] assim que o cabo estiver conectado, ou [Cancelar] para sair."
                    
        userResp = MsgBox(promptUsb, vbOKCancel + vbInformation, "Aura SCRCPY - Conectar Cabo USB")
        If userResp <> vbOK Then
            WScript.Quit
        End If
    Loop
    
    ' Dispositivo USB detectado: Desperta e lanca em ultra-desempenho
    WshShell.Run """" & adbPath & """ -d shell input keyevent 224", 0, True
    WshShell.Run """" & adbPath & """ -d shell wm dismiss-keyguard", 0, True
    WshShell.Run """" & adbPath & """ -d shell svc power stayon true", 0, True

    ' USB 90 FPS Zero Lag: Banda 24M, 1920p, 0 buffer (resposta instantanea)
    scrcpyCmd = """" & scrcpyPath & """ -d" & _
                " -S" & _
                " --stay-awake" & _
                " --video-codec=h265" & _
                " --video-bit-rate=24M" & _
                " --max-size=1920" & _
                " --max-fps=90" & _
                " --video-buffer=0" & _
                " --no-audio" & _
                " --disable-screensaver" & _
                " --shortcut-mod=lalt,lctrl" & _
                " --window-title=""Aura - USB (90fps - Zero Delay)"""

    WshShell.Run scrcpyCmd, 0, False
End Sub

' ==============================================================================
' FUNCAO: VERIFICA SE HA DISPOSITIVO USB FISICO CONECTADO
' ==============================================================================
Function CheckAuthorizedUsb(outputStr)
    Dim devLines, dLine, i
    CheckAuthorizedUsb = False
    devLines = Split(outputStr, vbCrLf)
    For i = 0 To UBound(devLines)
        dLine = Trim(devLines(i))
        If InStr(dLine, vbTab & "device") > 0 Or (InStr(dLine, "device") > 0 And InStr(dLine, "List of devices") = 0 And InStr(dLine, "offline") = 0 And InStr(dLine, "unauthorized") = 0) Then
            ' Dispositivos fisicos USB nao possuem porta TCP ':' nem prefixos mDNS
            If InStr(dLine, ":") = 0 And InStr(dLine, "adb-") = 0 And InStr(dLine, "._tcp") = 0 Then
                CheckAuthorizedUsb = True
                Exit Function
            End If
        End If
    Next
End Function