# UltiSim

Simulate FFXIV ultimate raid mechanics client-side for solo practice. Go to any Inn, open plugin with /ultisim, 
and start practicing!

**WARNING!!!**

This plugin is in beta and is unstable. You won't crash during your training sessions, but you will crash
after it. If you are doing any serious content: **RESTART YOUR GAME AFTER USING PLUGIN**.
You **WILL CRASH** in the middle of pull.

You don't have to disable plugin, just don't use it. I'm working on stability improvements, but it's not trivial.

## How to help
I want to release scenarios for UMAD as soon as possible, within week or two.
I will need ACT logs to do that. Please share them.

Pull requests welcome. 

I don't know what I'm doing. If you know anything about modding and know how to make things more stable,
please let me know.

## Installation

See: https://github.com/anomek/MyDalamudPlugins

## Details

* Spawns fake party members and boss NPCs into the live game client
* Drives their positions, cast bars, tethers, and VFX so mechanics play out visually
* Supports client-side zone loading — practice inside the instance without a full party

Currently implemented:
 - The Omega Protocol (Ultimate) — P5 Delta
 - The Omega Protocol (Ultimate) — P5 Sigma

## Known issues
* Crashes ;(
* Delta timings might be little bit off
* Omega-F rendering in sigma
* Tether distance threshold are very rough estimations

