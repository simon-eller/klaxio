# 🔘 KLAXIO
Since Amazon discontinued official support and skills for the Echo Buttons, many of these great devices have been gathering dust. **KLAXIO** gives your Echo Buttons a second life! 

KLAXIO is a standalone local web server and game manager that connects directly to your Amazon Echo Buttons via your laptop's Bluetooth. It turns them into responsive game show buzzers for a 2-player quiz setup, complete with scoring, visual feedback, and sound effects.

## ✨ Features
* **No Cloud Required:** Runs completely locally on your machine.
* **Instant Browser UI:** Automatically launches a beautiful, dark-themed dashboard to track scores and buzzer status.
* **Hardware Integration:** Reads Amazon Echo Buttons directly via Bluetooth.
* **Keyboard Hotkeys:** Control the flow of the game seamlessly as the host without using your mouse.
* **Bilingual:** UI supports English and German.

## 🚀 Installation & Setup
You don't need to compile the code yourself. You can download the ready-to-use executable directly from GitHub.

1. Go to the **[Releases](../../releases)** page on this GitHub repository.
2. Download the latest `klaxio.exe` file.
3. Save it to a folder on your computer.
4. Run `klaxio.exe`. (If Windows SmartScreen blocks it, click "More info" and "Run anyway").
5. A console window will open, and your default web browser will automatically launch the KLAXIO game board at `http://localhost:8765/`.

## 🔵 How to Connect Your Echo Buttons
Before starting the game, you need to pair your Echo Buttons to your Windows laptop. KLAXIO listens to the buttons via your system's built-in Bluetooth.

**Step 1: Put the Echo Button into Pairing Mode**
1. Insert fresh batteries into your Echo Button.
2. Press and hold the top of the Echo Button for about **10 to 15 seconds**.
3. Release the button when it starts glowing/flashing **orange**. It is now in pairing mode.

**Step 2: Pair with Windows**
1. Open your Windows **Settings** and go to **Bluetooth & devices**.
2. Make sure Bluetooth is turned **On**.
3. Click **Add device** and select **Bluetooth**.
4. Look for a device named **Echo Button** (or a similar alphanumeric name) and click it to pair.
5. Repeat this entire process for your second Echo Button.

**Step 3: Start the Game**
1. Once both buttons are paired to Windows, start `klaxio.exe`.
2. The console window will display that it is searching for paired devices and will confirm when it connects to your buttons.
3. The first button pressed will be assigned to "Player 1", and the second to "Player 2".

## 🎮 How to Play
As the host, you manage the game flow. You can use the buttons on the web interface, or use the following keyboard shortcuts while the web page or the console is in focus:

### Game Phases:
1. **READY / WAITING:** Standby mode.
2. **BUZZ! (Armed):** The buzzers are unlocked. The first player to hit their Echo Button locks out the other player.
3. **BUZZED:** A player has buzzed in and must give their answer.
4. **CORRECT / WRONG:** The host evaluates the answer, distributing points accordingly.

### Host Controls (Keyboard Shortcuts)
* <kbd>Space</kbd> or <kbd>Enter</kbd> / <kbd>A</kbd>: **Arm / Unlock Buzzers** (Start a new question)
* <kbd>+</kbd> or <kbd>1</kbd> / <kbd>C</kbd>: Mark Answer as **Correct** (+1 Point)
* <kbd>-</kbd> or <kbd>0</kbd> / <kbd>W</kbd>: Mark Answer as **Wrong**
* <kbd>R</kbd>: **Reset Phase** (Cancel the current question without awarding points)
* <kbd>Q</kbd>: **Quit** (Press inside the console window to shut down the server)

## 🛠️ Technology Stack
* **Backend:**
  * **C#**
  * **.NET 10**
* **Frontend:**
  * **[Bootstrap 5](https://getbootstrap.com/)** by The Bootstrap Authors
    Licenced under [MIT License](https://github.com/twbs/bootstrap/blob/main/LICENSE).
  * **HTML**
  * **CSS**
  * **JavaScript**
* **Bluetooth Handling:**
  * **[EchoButtons](https://github.com/zaront/EchoButtons)** by Zaron Thompson
    Licenced under [MIT License](https://github.com/zaront/EchoButtons/blob/master/LICENSE).

## Build self-contained EXE
If you want to, you could also run or compile from source by yourself.

```powershell
git clone https://github.com/simon-eller/klaxio.git
cd klaxio

dotnet run

dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true
```

Output: `bin\Release\net10.0-windows\win-x86\publish\klaxio.exe`  
Single file, no .NET installation needed on the target machine.
