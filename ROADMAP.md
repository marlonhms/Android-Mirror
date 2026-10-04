# 🚀 ROADMAP & GUIA DE MODERNIZAÇÃO: AURA SCRCPY v4.1 PRO

Este documento estabelece o diagnóstico técnico, a arquitetura visual, o plano de otimização inteligente e as fases de evolução do inicializador do **SCRCPY v4.1**, substituindo a antiga interface básica em VBScript por uma aplicação desktop de alta performance, tecnológica e acessível sem tocar em código.

---

## 📌 Sumário Executivo
- **Localização:** `.\scrcpy-win64-v4.1\`
- **Estado Anterior:** Script `Inicio Otimizado.vbs` executado via WScript, com caixas de diálogo `InputBox` e `MsgBox` (estilo visual legado do Windows 95), sem controles visuais de personalização e com o ícone genérico de script do Windows.
- **Novo Estado Implementado:** Executável nativo standalone `AuraScrcpyLauncher.exe` com tema Dark Cyber/Fluent, ícone oficial embutido em alta resolução (`scrcpy.ico`), atalhos `.lnk` para Área de Trabalho e pasta local, 5 perfis de otimização rápida com realce visual dinâmico, monitoramento hotplug USB em tempo real, cão de guarda (watchdog) com interceptação de falhas na inicialização, botão de encerramento rápido de processos e persistência total de parâmetros.

---

## 1. Diagnóstico Comparativo: VBScript Legado vs. Aura Launcher PRO

| Aspecto | Script Anterior (`Inicio Otimizado.vbs`) | Aura Launcher v4.1 PRO (`AuraScrcpyLauncher.exe`) |
| :--- | :--- | :--- |
| **Identidade Visual** | Ícone padrão do Windows Script Host (folha azul genérica). | Ícone oficial do SCRCPY embutido no `.exe` e nos atalhos `.lnk` (7 resoluções de 16x16 até 256x256). |
| **Interface com Usuário** | `InputBox` retangular cinza, fonte bitmap simples, bloqueante e monótona. | Janela Fluent/Cyberpunk com tema Dark Navy (`#0B0F19`), tipografia Segoe UI, botões com gradientes e badges de status. |
| **Acessibilidade a Ajustes** | O usuário precisava editar código VBS para alterar bitrate, resolução, FPS, buffer ou áudio. | 5 Presets de 1 clique, toggles visuais para ferramentas e painel expansível de "Ajustes Finos" com controles intuitivos. |
| **Detecção de Dispositivos** | Script congelava a tela enquanto aguardava `adb`. | Detecção USB em tempo real assíncrona (atualiza automaticamente a cada 3,5s ao plugar o cabo), exibindo o modelo real do aparelho. |
| **Tratamento de Erros** | Janelas de erro genéricas ou falha silenciosa. | **Watchdog de Inicialização:** Monitora os primeiros 1500ms do SCRCPY. Se o processo falhar (codec incompatível, falta de autorização USB, áudio não suportado), exibe um alerta claro com sugestão de solução. |
| **Controle de Processos** | Não oferecia botão na interface para parar a sessão. | Botão **"⏹️ Encerrar"** integrado na barra principal para finalizar processos do SCRCPY com 1 clique. |
| **Persistência de Ajustes** | Gravava apenas o último IP em arquivo de texto. | Salva **100% das opções** (modo, IP, perfil ativo, resolução, FPS, bitrate, codec, buffer, áudio, webcam, tela cheia) em `aura_settings.ini`. |

---

## 2. Identidade Visual & Solução do Ícone de Execução

### O Problema do Ícone `.vbs`
No Windows, arquivos com extensão `.vbs` são associados ao executável `wscript.exe`. O sistema operacional não permite que um arquivo de script possua um ícone próprio embutido diretamente no arquivo, resultando sempre no ícone genérico de script do sistema.

### Como foi solucionado:
1. **Geração do Arquivo de Ícone Multi-Resolução (`scrcpy.ico`):**
   - Criado a partir da matriz gráfica oficial `scrcpy.png`.
   - Incorpora 7 camadas de resolução com canal alfa (transparência de 32 bits):
     - `16x16`, `24x24`, `32x32`, `48x48`, `64x64`, `128x128` e `256x256` pixels.
     - Isso garante nitidez impecável na barra de tarefas, na área de trabalho e na visualização com ícones extragrandes do Windows Explorer.
2. **Compilação de Binário Nativo `.exe`:**
   - O código em C# (`AuraScrcpyLauncher.cs`) é compilado com o compilador nativo do Windows (`csc.exe`) utilizando a diretiva `/win32icon:scrcpy.ico`.
   - O ícone fica cravado diretamente no cabeçalho PE (Portable Executable) do arquivo binário.
3. **Criação de Atalhos `.lnk` (Desktop e Pasta Local):**
   - Criados atalhos diretos `Aura SCRCPY.lnk` com ícone oficial configurado e diretório de trabalho correto.

---

## 3. Matriz de Perfis de Otimização Inteligente

O usuário não precisa memorizar comandos de terminal. A aplicação oferece 5 perfis pré-configurados que ajustam instantaneamente todos os parâmetros:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               MATRIZ DE DESEMPENHO AURA                                │
├─────────────────────┬─────────┬─────────┬────────────┬──────────┬──────────┬───────────┤
│ Perfil              │ FPS     │ Bitrate │ Resolução  │ Codec    │ Buffer   │ Áudio     │
├─────────────────────┼─────────┼─────────┼────────────┼──────────┼──────────┼───────────┤
│ ⚡ Ultra Competitivo │ 90/120  │ 24 Mbps │ 1080p      │ H.265    │ 0 ms     │ Mudo      │
│ 🎮 Gaming Fluido    │ 90      │ 16 Mbps │ 1600p      │ H.265    │ 25 ms    │ Opus      │
│ ⚖️ Equilibrado      │ 60      │ 10 Mbps │ 1080p      │ H.265    │ 50 ms    │ Opus      │
│ 📺 Apresentação 2K  │ 60      │ 28 Mbps │ 2560p (2K) │ H.265    │ 50 ms    │ Opus + T  │
│ 🔋 Wi-Fi Econômico  │ 60      │ 6 Mbps  │ 720p (HD)  │ H.264    │ 80 ms    │ Mudo      │
│ 🛠️ Personalizado    │ Livre   │ Livre   │ Livre      │ Livre    │ Livre    │ Livre     │
└─────────────────────┴─────────┴─────────┴────────────┴──────────┴──────────┴───────────┘
```
*(Nota: No perfil Apresentação 2K, a opção `Toques na Tela (-t)` é ativada automaticamente para visualização pelo público).*

### Realce Visual & Modo Personalizado
- Ao clicar em um perfil, o botão correspondente recebe uma moldura colorida e preenchimento com brilho sutil (ex: Verde Esmeralda para Ultra, Azul Ciano para Gaming, Dourado para Equilibrado, Roxo para 2K).
- Se o usuário alterar manualmente qualquer controle no expander de "Ajustes Finos", o indicador de status atualiza imediatamente para **`Perfil: 🛠️ Personalizado`**, garantindo total fidelidade visual.

---

## 4. Recursos e Ferramentas do SCRCPY 4.1 Desbloqueados

Todas as principais ferramentas do SCRCPY 4.1 estão expostas por toggles e seletores intuitivos:

### 1. 📱 Desligar Tela Física do Celular (`-S` / `--turn-screen-off`)
- **O que faz:** Desliga o display AMOLED/LCD do celular assim que o espelhamento abre no monitor.
- **Benefício:** Economia massiva de bateria, redução drástica do calor do smartphone e preservação da tela contra burn-in.

### 2. ☕ Manter Aparelho Acordado (`-w` / `--stay-awake`)
- **O que faz:** Impede o sistema operacional Android de entrar em suspensão ou desligar a sessão por inatividade.

### 3. 🔊 Transmissão de Áudio de Baixa Latência (`--audio-codec=opus`)
- **O que faz:** Captura o áudio interno do Android (jogos, YouTube, chamadas) e encaminha para as caixas de som ou headset do computador via codec Opus de altíssima fidelidade. Requer Android 11+.

### 4. 📌 Janela Sempre no Topo (`--always-on-top`) & Sem Bordas (`--window-borderless`)
- **O que faz:** Fixa a tela do celular flutuando sobre qualquer janela do Windows e remove as molduras pesadas do sistema, ideal para quem trabalha com multitarefa.

### 5. 🖥️ Iniciar em Tela Cheia (`-f` / `--fullscreen`)
- **O que faz:** Inicia o espelhamento em tela cheia logo no primeiro segundo.

### 6. 👆 Exibir Toques Físicos na Tela (`-t` / `--show-touches`)
- **O que faz:** Renderiza pequenos círculos visuais nos locais onde os dedos tocam a tela, ideal para gravações de tutoriais e reuniões.

### 7. 🔴 Gravação Direta da Sessão em MP4 (`--record`)
- **O que faz:** Salva a transmissão diretamente em um arquivo MP4 de alta qualidade com carimbo de data e hora (`gravacao_AAAAMMDD_HHMMSS.mp4`) na pasta do SCRCPY, dispensando softwares pesados como OBS.

### 8. 📷 Modo Câmera / Webcam de Estúdio (`--video-source=camera`)
- **O que faz:** Utiliza os sensores ópticos físicos do celular como uma webcam de estúdio profissional para Discord, Teams, Zoom e OBS, com seletor direto de **Câmera Traseira** ou **Câmera Frontal**. Requer Android 12+.

### 9. ⌨️ Modo OTG (`--otg`)
- **O que faz:** Simula teclado e mouse USB conectados fisicamente ao aparelho via hardware, sem transmitir vídeo. Protegido na interface: desabilita automaticamente no modo Wi-Fi para evitar travamentos de conexão.

---

## 5. Guia Integrado de Atalhos do SCRCPY

A interface inclui um guia expansível com os atalhos de teclado mais úteis do SCRCPY:

| Atalho de Teclado | Ação Realizada no Android |
| :--- | :--- |
| **`Alt + P`** | Liga / Desliga a tela do celular (Power) |
| **`Alt + O`** | Apaga a tela física mantendo o espelhamento ativo no PC |
| **`Alt + F` ou `F11`** | Alterna entre Modo Janela e Tela Cheia |
| **`Alt + H`** | Pressiona o botão Home (Início) |
| **`Alt + B` ou Botão Direito** | Pressiona o botão Voltar (Back) |
| **`Alt + S`** | Abre o menu de Aplicativos Recentes (Multitarefa) |
| **`Alt + C` / `Alt + V`** | Sincroniza a Área de Transferência (Copiar e Colar) |
| **`Alt + I`** | Ativa / Desativa o contador de FPS em tempo real nos logs |
| **Arrastar arquivo `.apk`** | Instala o aplicativo automaticamente no smartphone |
| **Arrastar qualquer arquivo** | Envia o arquivo para a pasta `/sdcard/Download/` do aparelho |

---

## 6. Arquitetura Técnica e Robustez de Execução

```
[ Usuário ] 
    │
    ▼ (Clique Duplo no Ícone)
[ AuraScrcpyLauncher.exe ] (C# .NET WPF Nativo)
    ├── UI Fluent Dark com Alto Contraste (Texto Slate Claro sobre Dark Navy)
    ├── Hotplug Timer: Polling assíncrono a cada 3.5s (identifica conexão/desconexão USB)
    ├── Smart TCP/IP: Valida presença de USB e consulta wlan0 via 'ip addr' e 'ip route'
    ├── Ping ICMP Probe: Medição rápida de latência de rede com diagnóstico em cores
    ├── Config Manager: Gravação/Leitura completa em 'aura_settings.ini'
    │
    ▼ (Ao Clicar em "INICIAR ESPELHAMENTO")
[ Watchdog de Inicialização ]
    ├── Desperta o dispositivo via ADB (Keyevent 224 + Dismiss Keyguard)
    ├── Executa scrcpy.exe com captura assíncrona de Stderr (sem deadlocks)
    ├── Monitora os primeiros 1500ms:
    │     ├── Se encerrar com erro: Captura mensagem e exibe diagnóstico acionável ao usuário
    │     └── Se mantiver ativo: Exibe status de sucesso com PID e fecha painel se configurado
```

### Principais Proteções de Engenharia Implementadas:
1. **Eliminação de Deadlocks de Pipes:** Uso de eventos assíncronos `OutputDataReceived` e `ErrorDataReceived` com `AutoResetEvent` em vez de leituras síncronas bloqueantes.
2. **Proteção Contra Falha Silenciosa:** Se o usuário tentar rodar em modo USB sem celular plugado, ou se o codec H.265 não for suportado pela GPU do celular, o launcher não fecha; ele abre uma janela explicativa indicando o que ajustar.
3. **Validação Estrita de Wi-Fi e OTG:** O botão "Ativar Wi-Fi" verifica se o aparelho USB está com autorização ativa antes de tentar rodar `adb tcpip 5555`, evitando falsos avisos de sucesso.

---

## 7. Roadmap de Evolução em Fases

### 🟢 FASE 1: Fundação, Estabilidade & Interface Moderna (CONCLUÍDA)
- [x] Criação do ícone nativo multi-resolução `scrcpy.ico` (16x16 até 256x256 com canal alfa).
- [x] Criação e compilação do executável nativo standalone `AuraScrcpyLauncher.exe`.
- [x] Interface gráfica Fluent/Cyberpunk com tema Dark, cantos suaves e tipografia moderna.
- [x] Correção de contraste e renderização de textos dos ComboBoxes no tema escuro.
- [x] Abas independentes para Conexão USB e Conexão Wi-Fi com detecção de hardware.
- [x] Monitoramento automático Hotplug USB em tempo real a cada 3,5 segundos.
- [x] Sistema de 5 Perfis Rápidos de Otimização com realce visual individual por cores.
- [x] Modo "Perfil: Personalizado" com sincronização automática quando qualquer seletor é ajustado.
- [x] Persistência completa de 100% dos parâmetros (toggles, combos, IP e perfis) em `aura_settings.ini`.
- [x] Watchdog de inicialização com diagnóstico amigável para falhas de codec, áudio e autorização.
- [x] Botão "⏹️ Encerrar SCRCPY" para finalizar instâncias ativas ou processos órfãos.
- [x] Ferramenta "Ativar Wi-Fi (TCP/IP)" com validação de USB e descoberta inteligente de IP via `wlan0`.
- [x] Medição de latência (Ping ICMP) com diagnóstico visual por faixas de milissegundos.
- [x] Guia rápido interativo de atalhos de teclado do SCRCPY embutido na janela.
- [x] Botões para Tela Cheia (`-f`), Exibir Toques (`-t`) e Câmera Frontal/Traseira.

---

### 🟡 FASE 2: Automação Inteligente de Rede & Múltiplos Aparelhos
- [x] **Emparelhamento Sem Fio Moderno (Android 11+ `adb pair`):**
  - Assistente visual modal nativo para digitar o código de pareamento de 6 dígitos da Depuração por Wi-Fi do Android moderno, dispensando a necessidade de conectar o cabo USB na primeira vez.
- [x] **Auto-Detecção de Dispositivos Wi-Fi via mDNS:**
  - Varredura assíncrona automática via `adb mdns services` para capturar a porta dinâmica gerada pelo Android ao ligar a Depuração Sem Fio, preenchendo e conectando com 1 clique no botão `🔍 Auto-Detectar`.
- [x] **Fixação Inteligente de Porta 5555 (`📲 Fixar Porta 5555`):**
  - Permite fixar o daemon ADB na porta padrão 5555 direto pela conexão sem fio ou USB, eliminando dependência de portas aleatórias.
- [ ] **Histórico & Gerenciador de Múltiplos Dispositivos:**
  - Seletor suspenso de aparelhos conectados para alternar facilmente entre celular pessoal, celular de trabalho ou tablet.
  - Salvar perfis individuais memorizados por aparelho (ex: "Galaxy S23", "Tablet").
- [ ] **Detecção Automática de Proporção de Tela:**
  - Consultar `adb shell wm size` ao conectar para calcular a proporção exata e sugerir a melhor resolução nativa sem barras pretas no monitor do PC.

---

### 🔵 FASE 3: Central Multimídia & Produtividade Avançada
- [ ] **Painel Interativo de Webcam:**
  - Botão na barra para acender e apagar a lanterna/flash do celular remotamente (`MOD+t`).
  - Controles de zoom óptico da câmera direto na tela do computador (`MOD+Up` / `MOD+Down`).
- [ ] **Central de Vídeos Gravados:**
  - Botão "Abrir Pasta de Gravações" para acessar rapidamente os arquivos `.mp4` gerados pela sessão.
  - Notificação sonora sutil de início e término de gravação.
- [ ] **Bandeja do Sistema (System Tray / Ícone ao lado do relógio):**
  - Opção para minimizar o Aura Launcher para a bandeja do sistema (perto do relógio do Windows) com menu de contexto de início rápido para espelhamento em 1 clique.

---

## 8. Como Utilizar a Ferramenta Agora

1. **Abrir a Aplicação:**
   - Dê um duplo clique no atalho **`Aura SCRCPY.lnk`** na sua **Área de Trabalho** ou na pasta da ferramenta.
2. **Modo Cabo USB (Recomendado para Jogos e 90 FPS Zero Delay):**
   - Conecte o celular com o cabo USB. O launcher detectará seu aparelho em até 3 segundos e exibirá a luz verde 🟢.
   - Escolha o perfil desejado (ex: *Ultra Competitivo*) e clique em **🚀 INICIAR ESPELHAMENTO**.
3. **Modo Wi-Fi Sem Fio (Para Liberdade Total):**
   - Conecte o cabo USB uma única vez e clique em **"📲 Ativar Wi-Fi (TCP/IP)"**. O launcher configurará a porta 5555 e preencherá seu IP automaticamente.
   - Desconecte o cabo, clique em **"⚡ Testar Latência (Ping)"** para checar a saúde da rede e clique em **🚀 INICIAR ESPELHAMENTO**.
4. **Encerrar Sessões:**
   - Para finalizar uma transmissão ou fechar instâncias travadas do SCRCPY a qualquer momento, clique no botão vermelho **"⏹️ Encerrar"** na janela do Aura Launcher.
