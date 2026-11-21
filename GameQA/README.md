# Game QA

Game QA runs repeatable smoke, menu, and basic functionality tests against a packaged Windows game. Run GameQA.exe. To rebuild from source, run build.cmd on 64-bit Windows with .NET Framework 4.x.

## First smoke test

1. Select the game executable and enter any launch arguments. Use a dedicated QA profile or save if the game supports one. Set the game to windowed or borderless mode.
2. Choose Record to start the game. Navigate the menus, then press F10 while the game is focused to stop. The recorder captures keyboard and left/right mouse presses and releases with delays between them, including overlapping holds. F10 ends the recording.
3. Navigate the running game to a screen you want to check, then choose Capture target and drag around a distinctive, static UI element. Save the PNG. Capture fills the selected image step or adds a new Wait for image step.
4. Edit the steps and replace loading delays with Wait for image checks. To target a button, set the action to Click image. Use Move up and Move down to put each check in the correct order. Use Expect exit after the action that quits the game.
5. Save the suite as JSON, close the recording session, and choose Run suite. Leave the game in the foreground during playback. If a run stops or fails, close the game before rerunning.

| Action | Target or value | Seconds |
| --- | --- | --- |
| Wait for image | PNG path | Timeout |
| Click image | PNG path | Timeout |
| Click point | Client-area coordinates, such as 420,250 | Blank |
| Input down / Input up | Key name, LButton, or RButton | Blank |
| Move pointer | Client-area X,Y | Blank |
| Press key | Windows key name, such as Escape, Enter, or W | Blank |
| Wait | Blank | Delay |
| Expect exit | Blank | Timeout for a successful process exit |

Save the PNGs alongside the suite or in a subfolder to keep those paths portable. Select the executable again when moving a suite to a machine with a different installation path. Suites can also be edited as JSON with the desktop app closed.

The Windows screen capture and input simulation require an interactive desktop. Use the same game resolution and UI scale at capture and replay. The recorder samples input about every 20 ms, so very fast transitions can be missed. Continuous mouse movement, gamepads, launcher child processes, and engine-state assertions are outside this prototype. Clear recording removes every step from the editor after confirmation. The result panel shows green for a pass and red for a failure. Failure reports go to the GameQA/Failures folder under Local AppData, with a game screenshot when the game window is available and focused.

## Verification

Run GameQA.exe --self-test and check the process exit code. Zero indicates that the matcher found the expected image center and rejected an absent target.
