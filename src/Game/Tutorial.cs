using System.Collections.Generic;
using Renegade.Ui;

namespace Mancala.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Fill your store",
			"Your six pits are the near row; your store is the big hole on the right, painted gold. Your opponent's store, on the left, is blue. Most marbles in your store at the end wins.",
			"res://tutorial/sow.ogv"),
		new TutorialPage("Sow the marbles",
			"Tap one of your glowing pits. Its marbles are dropped one at a time into the holes that follow, counter-clockwise: into your store too, but never your opponent's.",
			"res://tutorial/sow.ogv"),
		new TutorialPage("Go again",
			"If your last marble lands in your own store, you take another turn straight away. Plan for it: a chain of free turns can win the game.",
			"res://tutorial/sow.ogv"),
		new TutorialPage("Capture",
			"If your last marble lands in an empty pit on your side, you capture it, and every marble in the pit opposite. They fly into your store.",
			"res://tutorial/capture.ogv"),
		new TutorialPage("The end",
			"When one side's pits are all empty, the game ends and each player keeps the marbles left on their side. The winner's store catches fire. Settings has the pit numbers, store totals and marble shape.",
			"res://tutorial/end.ogv"),
	};
}
