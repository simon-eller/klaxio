# Klaxio
Since Amazon discontinued official support and skills for the Echo Buttons, many of these great devices have been gathering dust. Klaxio gives your Echo Buttons a second life!

Klaxio is a standalone local web server and game manager that connects directly to your Amazon Echo Buttons via your laptop's Bluetooth. It turns them into responsive game show buzzers, complete with scoring, visual feedback and a final ranking.

It ships with two games modes

* **Classic Quiz**: You ask the questions, the first player to hit their button gets to answer.
* **Music Quiz**: A "name that tune" round. Songs stream straight from a YouTube playlist inside the page; players buzz in to name the track.

## ✨ Features
* **Beautiful UI**: Great looking dashboard to track scores and buzzer status.
* **Multiplayer**: Play with two or more Echo Buttons.
* **Final Ranking**: Reveal a podium at the end of a game.
* **Keyboard Hotkeys**: Control the flow of the game as the host without using your mouse.
* **Curated playlists**: Offer your own set of ready-made playlists, maintained in a simple JSON file.
* **Bilingual**: English and German are supported.

## 🚀 Installation & Setup
You don't need to compile the code yourself. You can download the ready-to-use executable directly from GitHub.

1. Go to the **[Releases](../../releases)** page on this GitHub repository.
2. Download the latest `klaxio-win-x86.exe` file.
3. Save it to a folder on your computer.
4. Run `klaxio-win-x86.exe`. (If Windows SmartScreen blocks it, click "More info" and "Run anyway").
5. A console window will open, and your default web browser will automatically launch the Klaxio game board at `http://localhost:8765/`.

## How to Connect Your Echo Buttons
Before starting the game, you need to pair your Echo Buttons to your Windows laptop.
Klaxio listens to the buttons via your system's built-in Bluetooth.

**Step 1: Put the Echo Button into Pairing Mode**
1. Insert fresh batteries into your Echo Button.
2. Press and hold the top of the Echo Button for about **10 to 15 seconds**.
3. Release the button when it starts glowing/flashing **orange**. It is now in pairing mode.

**Step 2: Pair with Windows**
1. Open your Windows **Settings** and go to **Bluetooth & devices**.
2. Make sure Bluetooth is turned **On**.
3. Click **Add device** and select **Bluetooth** (maybe you have to press "show more devices").
4. Look for a device named **Echo Button** (or a similar alphanumeric name) and click it to pair.
5. Repeat this entire process for every Echo Button you want to use.

**Step 3: Register the Buttons**
1. Once the buttons are paired to Windows, start `klaxio-win-x86.exe`.
2. The console window will display that it is searching for paired devices and will confirm when it connects to your buttons.
3. The browser opens on **Players and buttons**. Every player presses their Echo Button **once** to claim a seat.
4. Give the players proper names if you like, then press **Done**.

You can reopen this screen at any time via **Settings → Players and buttons**, for example to add a latecomer or to let somebody sit out a round.

## 🎮 How to Play

### Classic Quiz
1. **READY:** Standby. Ask your question.
2. **BUZZ!:** Unlock the buzzers. The first player to hit their Echo Button locks out everyone else.
3. **BUZZED:** That player answers.
4. **CORRECT / WRONG:** You judge the answer. **Correct** gives the buzzing player a point, **Wrong** gives a point to everybody else.
5. **End Game:** Reveals the podium and the full ranking.

### Music Quiz
1. Pick a playlist under **Settings → Klaxio Music**: either click one of the ready-made playlists (see [Curated playlists](#-curated-playlists)) or paste any YouTube or YouTube Music playlist URL. Choose how many rounds to play. The playlist is validated and shuffled straight away.
2. Press **Start game** on the Klaxio Music board. The first track starts playing right away.
3. **Play Song** starts every following track. Title, artist and cover art stay hidden while it plays.
4. The first player to buzz gets to answer; **Show Answer** then reveals the track.
5. **Correct** gives the buzzing player a point, **Wrong** gives a point to everybody else.
6. After the last round the podium appears, together with a list of every track that was played.

## 🎵 Curated playlists
The playlists offered on the settings screen live in **`playlists.json`**, in the same folder as
`klaxio-win-x86.exe`. Edit that file and reload the browser - no rebuild and no restart needed.

**Downloading the exe is enough.** A copy of the list is built into the executable, so the ready-made
playlists work out of the box without any extra file. Only add `playlists.json` next to the exe if you
want to replace that built-in list with your own - the file then takes over completely.

```json
{
  "categories": [
    {
      "name": "Charts",
      "icon": "trending_up",
      "playlists": [
        { "name": "Popular Music Videos", "url": "https://www.youtube.com/playlist?list=PLFgquLnL59alCl_2TQvOiD5Vgm1hCaGSI" },
        { "url": "https://music.youtube.com/playlist?list=PL15B1E77BB5708555" }
      ]
    }
  ]
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `categories[].name` | no | Heading above the row of tiles. |
| `categories[].icon` | no | Any [Material Symbols](https://fonts.google.com/icons) name, e.g. `trending_up`. Defaults to `queue_music`. |
| `playlists[].url` | **yes** | YouTube or YouTube Music playlist URL. |
| `playlists[].name` | no | Label under the tile. Falls back to the playlist title on YouTube - handy to shorten long ones. |
| `playlists[].thumbnail` | no | Cover image URL. Falls back to the playlist's cover art on YouTube. |

Missing names and cover art are read once from the playlist page on YouTube and then cached, so entries
usually only need a `url`. For a YouTube Music playlist this is its real cover art; for an ordinary playlist
it is the same cover YouTube itself shows, which is the first video's image. Setting `thumbnail` yourself
skips the lookup entirely. If a lookup fails the tile still works - it just shows a placeholder cover.
Playlists listed twice are ignored, and if `playlists.json` is deleted the copy built into the executable is used
again. A file that is not valid JSON leaves the selection empty rather than silently falling back - the console
says which file failed and why.

### Host Controls (Keyboard Shortcuts)
The shortcuts follow whichever game is on screen. They work in the browser window and in the console window.

| Key | Klaxio | Klaxio Music |
| --- | --- | --- |
| <kbd>Space</kbd> / <kbd>Enter</kbd> / <kbd>A</kbd> | Unlock buzzers | Play the next song |
| <kbd>+</kbd> / <kbd>1</kbd> / <kbd>C</kbd> | Correct (+1 point) | Correct (+1 point) |
| <kbd>-</kbd> / <kbd>0</kbd> / <kbd>W</kbd> | Wrong (point for everybody else) | Wrong (point for everybody else) |
| <kbd>S</kbd> | – | Skip the current song |
| <kbd>R</kbd> | New question | Reveal the answer |
| <kbd>Q</kbd> | Quit (in the console window) | Quit (in the console window) |

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
  * **[YouTube IFrame Player API](https://developers.google.com/youtube/iframe_api_reference)** (Klaxio Music playback)
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

Output: `bin\Release\net10.0-windows\win-x86\publish\klaxio-win-x86.exe`  
Single file, no .NET installation needed on the target machine.
