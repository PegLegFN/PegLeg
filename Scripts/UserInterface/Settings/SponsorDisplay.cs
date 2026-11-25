using Godot;
using System.Linq;

public partial class SponsorDisplay : Node
{
	[Export]
	Control epicListSection;
	[Export]
	Node epicListNode;
	IListHandler epicList;

	[Export]
	Control legListSection;
	[Export]
	Node legListNode;
	IListHandler legList;

	[Export]
	Control mythicListSection;
	[Export]
	Node mythicListNode;
	IListHandler mythicList;

	[Export]
	Control errorContent;

	EntryList<SponsoredUser> epicSponsors = [];
	EntryList<SponsoredUser> legSponsors = [];
	EntryList<SponsoredUser> mythicSponsors = [];

	public override void _Ready()
	{
		epicList = (IListHandler)epicListNode;
		epicList.LinkListProvider(epicSponsors);

		legList = (IListHandler)legListNode;
		legList.LinkListProvider(legSponsors);

		mythicList = (IListHandler)mythicListNode;
		mythicList.LinkListProvider(mythicSponsors);
		RefreshTimerController.OnDayChanged += RefreshSponsorList;
		RefreshSponsorList();
	}

	public override void _ExitTree()
	{
		RefreshTimerController.OnDayChanged -= RefreshSponsorList;
	}
	public struct SponsoredUser
	{
		public string username { get; init; }
		public string avatar { get; init; }
		public int tier { get; init; }
	}

	async void RefreshSponsorList()
	{
		var response = await ApiWebAddresses.pegLegLiteBucket.MakeRequest("displaySponsors.json").Send();
		if (await response.CheckForError())
		{
			//show error notice
			errorContent.Visible = true;

			epicSponsors.Clear();
			legSponsors.Clear();
			mythicSponsors.Clear();

			epicListSection.Visible = false;
			legListSection.Visible = false;
			mythicListSection.Visible = false;
			return;
		}
		//hide error notice
		errorContent.Visible = false;
		var sponsorList = await response.ReadJson<SponsoredUser[]>();

		if (Bootstrap.IsEditor)
		{
			sponsorList = [..sponsorList,
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=3,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=3,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=3,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=2,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=2,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=2,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=2,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=2,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				},
				new(){
					username="TomatechGames",
					avatar="https://avatars.githubusercontent.com/u/51051235?v=4",
					tier=1,
				}
			];
		}

		epicSponsors.Clear();
		epicSponsors.AddRange(sponsorList.Where(u => u.tier <= 1));
		epicListSection.Visible = epicSponsors.Count > 0;
		epicList.UpdateList();

		legSponsors.Clear();
		legSponsors.AddRange(sponsorList.Where(u => u.tier == 2));
		legListSection.Visible = legSponsors.Count > 0;
		legList.UpdateList();

		mythicSponsors.Clear();
		mythicSponsors.AddRange(sponsorList.Where(u => u.tier >= 3));
		mythicListSection.Visible = mythicSponsors.Count > 0;
		mythicList.UpdateList();
	}
}
