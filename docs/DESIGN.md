# Mancala: design

Store title: **Mancala**. Package `com.renegadegames.mancala`.

## The game

The classic two-row count-and-capture game (the "Kalah" rules most people know):

- Two rows of six **pits**, one row each, with a big **store** at each end; yours is on your right.
- Each pit starts with **4 stones** (48 in all).
- On your turn, pick up **all** the stones from one of your pits and **sow** them one at a time
  into the following pits, counter-clockwise, including your own store but **skipping your
  opponent's store**.
- **Last stone in your store**: you go again.
- **Last stone in an empty pit on your side**, and the pit opposite has stones: you **capture**
  that last stone and everything opposite, straight into your store.
- When either player's six pits are all empty, the game ends; each player banks the stones left
  on their own side. **Most stones in the store wins.**

## Feel

Counter Fire's lesson: real-feeling pieces, honest sounds, one unforgettable effect.

- The board lies **horizontally**, the traditional way round (your row nearest you, your store
  on your right); **drag sideways to spin it**, as in Counter Fire. Deep round pits; the stores are
  big rounded rectangles running across the board.
- The stones are **marbles**: perfect spheres, all one colour, set by the material you choose
  (Style menu, shared with Counter Fire: Classic, Oak, Steel, Glass, Marble for the board; Red,
  Walnut, Silver, Sapphire, Ivory for the marbles). Each material has its own sound.
- **Sowing** is visible: the stones lift out of the pit together, then drop one at a time into
  each pit along the way, each landing with the sound of its material (marble on marble, or on
  the board).
- **Signature effect, the capture**: sparks burst from the pit, then the captured marbles flare with
  light and stream into your store in glowing arcs ("Capture! +5"). Landing in
  your own store for an extra turn makes the store pulse.
- **Counts** float above each pit and store, so nobody has to count marbles by eye.

## Modes

- Player vs player on one phone.
- Player vs computer: **Easy** (mostly random, takes obvious extra turns), **Medium** (looks a few
  moves ahead), **Hard** (alpha-beta search, deep enough to be properly tough on a phone).

## Ads

Same as Counter Fire, from renegade-core:
- Between-games interstitial every 5th finished game.
- **Undo (ad)** vs the computer: a rewarded ad takes back your last turn (and the computer's reply).

## Scope for version 1

- Kalah rules, 4 stones per pit; PvP and PvC with three levels; undo vs computer.
- Five board and marble materials; sowing, capture and extra-turn effects; counts; sounds; spin.
- Settings: sound on/off, mode, difficulty.

## Later

- Rule options: 3/5/6 stones per pit; the "empty capture" variant.
- Other mancala families (Oware, Bao).
