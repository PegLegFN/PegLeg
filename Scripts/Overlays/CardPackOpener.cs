using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

public partial class CardPackOpener : Control
{
	public static CardPackOpener Instance { get; private set; }
	public static event Action OnLlamaOpeningComplete;
	[Export]
	float pullTime = 0.25f;
	[Export]
	float pullHoldTime = 1;
	[Export]
	float pullFastSpeed = 3;
	[Export]
	bool sortByRarity = false;
	[Export]
	Control displayPanel;
	[Export]
	AudioStreamPlayer music;
	[Export]
	Control stacheButton;
	[Export]
	Control pullButton;
	[Export]
	Control skipAllButton;
	[Export]
	Color defaultBackgroundColor;
	[Export]
	Control glowFlare;

	[ExportGroup("Llama")]
	[Export]
	int minLlamaImpacts = 3;
	[Export]
	int impactParticlePool;
	[ExportSubgroup("Sounds")]
	[Export]
	AudioStreamPlayer[] greetingEffects;
	[Export]
	AudioStreamPlayer[] greetingVoices;
	[Export]
	AudioStreamPlayer hoverEffect;
	[Export]
	AudioStreamPlayer impactEffect;
	[Export]
	AudioStreamPlayer impactVoice;
	[Export]
	AudioStreamPlayer miniImpactVoice;
	[Export]
	AudioStreamPlayer burstEffect;
	[Export]
	AudioStreamPlayer burstVoice;
	[Export]
	AudioStreamPlayer miniBurstVoice;
	[ExportSubgroup("Nodes")]
	[Export]
	PackedScene impactParticlesScene;
	[Export]
	Control impactParticleParent;
	[Export]
	Control llamaGlow;
	[Export]
	Control standardLlamaButton;
	[Export]
	CpuParticles2D confettiParticles;
	[Export]
	CardPackEntry llamaEntry;
	[Export]
	Control fullLlama;
	[Export]
	Control standardLlamaPartParent;
	Control[] standardLlamaParts;
	[Export]
	Control smallLlamaPartParent;
	Control[] smallLlamaParts;

	[ExportGroup("Cards")]
	[Export]
	GameItemEntry topCard;
	[Export]
	GameItemEntry prevCard;
	[Export]
	Control mainCardsParent;
	[Export]
	Control smallCardsOffset;
	[Export]
	int gapBetweenCards = 25;
	[Export]
	VBoxContainer smallCardParent;
	[Export]
	GameItemEntry[] smallCards = [];

	[ExportGroup("Choices")]
	[Export]
	ChoiceCardEntry[] choiceCardEntries = [];
	[Export]
	Control resultDestination;
	[Export]
	Control skipChoiceButton;
	[Export]
	Control cancelChoiceButton;
	[Export]
	Control singleChoiceEndContent;


	ShaderHook cardChangeEffect;
	public static bool IsOpen => Instance?.isOpen == true;
	bool isOpen;
	bool isSmall;
	bool cardPacksPrepared;
	bool singleChoiceMode;
	bool llamaBurstComplate;
	int llamaHits;
	Control fromPanel;
	GameItem defaultLlamaItem = GameItemTemplate.Get("CardPack:cardpack_bronze")?.CreateInstance();
	Control[] impactParticleContainers;
	CpuParticles2D[] impactParticles;
	int llamaTier = 0;
	int nextPullIndex = 1;
	bool choicesOnly;
	bool isPulling;
	bool isFast;
	bool waitForFirstHit = false;
	bool shouldStacheLlamas;


	public List<GameItem> queuedChoices = [];
	public List<GameItem> queuedItems = [];
	int TotalQueueLength => queuedChoices.Count + (choicesOnly ? 0 : queuedItems.Count);

	public override void _Ready()
	{
		Instance = this;
		standardLlamaParts = standardLlamaPartParent.GetChildren().Select(n => n as Control).ToArray();
		smallLlamaParts = smallLlamaPartParent.GetChildren().Select(n => n as Control).ToArray();
		cardChangeEffect = topCard.GetNode<ShaderHook>("%ChangeEffect");
		Visible = true;
		displayPanel.Visible = false;
		for (int i = 0; i < choiceCardEntries.Length; i++)
		{
			var index = i;
			choiceCardEntries[i].Visible = false;
			choiceCardEntries[i].Pressed += () => ApplyChoice(index);
		}
		ProcessMode = ProcessModeEnum.Disabled;

		impactParticlePool = Mathf.Max(impactParticlePool, 1);
		impactParticles = new CpuParticles2D[impactParticlePool];
		impactParticleContainers = new Control[impactParticlePool];
		for (int i = 0; i < impactParticlePool; i++)
		{
			var impactParticleContainer = impactParticlesScene.Instantiate() as Control;
			impactParticleParent.AddChild(impactParticleContainer);
			impactParticleContainers[i] = impactParticleContainer;
			impactParticles[i] = impactParticleContainer.GetChild<CpuParticles2D>(0);
			int index = i;
			llamaEntry.GradientChanged += g => impactParticles[index].ColorInitialRamp = g;
		}
	}

	public async Task StartOpening(GameItem[] cardPacks, Control fromPanel, GameItem llamaItem = null) => await StartOpening(cardPacks, fromPanel, null, 0, false, llamaItem);
	public async Task StartOpening(GameItem[] cardPacks, Control fromPanel, GameOffer llamaOffer, int llamaOfferQuantity, bool skipReveal, GameItem llamaItem = null)
	{
		if (isOpen)
		{
			GD.Print("Still Open");
			return;
		}

		var account = GameAccount.ActiveAccount;
		if (!await account.Authenticate(true, false))
			return;

		LoadingOverlay.TaskToken stacheLoadingToken = null;

		try
		{
			ProcessMode = ProcessModeEnum.Inherit;
			isOpen = true;
			//await this.WaitForFrame();

			stacheButton.Visible = llamaOffer?.IsXRayLlama == false;
			shouldStacheLlamas = false;
			llamaHits = 0;
			cardPacks ??= [];
			if (llamaItem is not null)
				llamaItem = llamaItem.Clone();
			llamaItem ??= llamaOffer?.itemGrants[0];
			if (cardPacks.Length > 0)
				llamaItem ??= cardPacks[^1];
			llamaItem ??= defaultLlamaItem;
			if (llamaOffer is not null)
			{
				llamaTier = llamaOffer.GetLocalXRayLlamaData(account)?.attributes?["highest_rarity"]?.GetValue<int>() ?? 0;
				if (llamaItem.template.DisplayName.Contains("Legendary"))
					llamaTier = 2;
				llamaItem.customData["llamaTier"] = llamaTier;
				GD.Print($"Offer Tier: {llamaTier}");
			}
			else
			{
				llamaTier = llamaItem.customData?["llamaTier"]?.GetValue<int>() ?? 0;
				GD.Print($"Item Tier: {llamaTier}");
			}
			llamaEntry.SetItem(llamaItem);
			isSmall = llamaItem.templateId == "CardPack:cardpack_basic";
			waitForFirstHit = true;
			//bgFade.TweenProperty(backgroundImage, "self_modulate", Colors.White, 0.25f);
			displayPanel.Visible = true;
			pullButton.Visible = false;
			skipAllButton.Visible = false;
			singleChoiceEndContent.Visible = false;
			cardPacksPrepared = false;
			llamaBurstComplate = false;
			this.fromPanel = fromPanel;
			glowFlare.Scale = Vector2.Zero;
			fullLlama.Visible = true;
			fullLlama.Scale = Vector2.One;
			LlamaScale(false);
			smallLlamaPartParent.Visible = false;
			standardLlamaPartParent.Visible = false;
			standardLlamaButton.Visible = false;

			topCard.Scale = Vector2.Zero;
			confettiParticles.Restart();
			confettiParticles.Emitting = false;
			for (int i = 0; i < impactParticles.Length; i++)
			{
				var particles = impactParticles[i];
				if (particles is null)
					continue;
				particles.Restart();
				particles.Emitting = false;
			}

			//start llama animation
			ResizePanelOpen();
			await Helpers.WaitForTimer(0.31);
			while (waitForFirstHit)
			{
				await Helpers.WaitForFrame();
			}
			stacheButton.Visible = false;
			if (shouldStacheLlamas)
			{
				stacheLoadingToken = LoadingOverlay.CreateToken();
			}

			GameItem[] extraItems = null;
			GameItem[] extraCardPacks = null;

			if (llamaOffer is not null)
			{
				var shopNotif = await account.PurchaseOffer(llamaOffer, llamaOfferQuantity);
				if (shopNotif is null)
				{
					await GenericConfirmationWindow.ShowConfirmation("Oops", "", "Close", "Failed to purchase Llama", allowCancel: false);
					stacheLoadingToken?.Dispose();
					CloseMenu();
					return;
				}
				var shopResultItems = shopNotif
					.First(val => val["type"].ToString() == "CatalogPurchase")["lootResult"]["items"]
						.AsArray()
						.Select(var => var.AsObject()
						)
					.ToArray();


				extraItems = [.. shopResultItems
					.Where(val => val?["itemGuid"] is not null && !(val["itemType"]?.ToString().StartsWith("CardPack") ?? false))
					.Select(val => GameItemTemplate.Get(val["itemType"].ToString())?.CreateInstance(
						(int)val["quantity"],
						val["attributes"]?.SafeDeepClone().AsObject(),
						account.GetProfile(val["itemProfile"].ToString()).GetItem(val["itemGuid"].ToString())
					))
				];

				extraCardPacks = [.. shopResultItems
					.Where(val => val.AsObject().ContainsKey("itemGuid") && (val["itemType"]?.ToString().StartsWith("CardPack") ?? false))
					.Select(val => account.GetProfile(val["itemProfile"].ToString()).GetItem(val["itemGuid"].ToString()))
				];
			}

			if (shouldStacheLlamas)
			{
				stacheLoadingToken.Dispose();
				CloseMenu();
				return;
			}
			extraItems ??= [];
			extraCardPacks ??= [];
			extraCardPacks = [.. extraCardPacks.Union(cardPacks)];

			//step 1: separate the choice cardpacks from the regular ones
			List<GameItem> openableCardPacks = [];
			foreach (var item in extraCardPacks)
			{
				if (!item.attributes.ContainsKey("options"))
					openableCardPacks.Add(item);
			}
			extraCardPacks = [.. extraCardPacks.Except(openableCardPacks)];

			//step 2: send request to open all regular ones
			if (openableCardPacks.Count > 0)
			{
				JsonArray cardpacksToOpen = new(default, openableCardPacks.Select(item => (JsonNode)item.uuid).ToArray());
				GD.Print("opening all these cardpacks:\n- " + openableCardPacks.Select(item => item.uuid).ToArray().Join("\n- "));
				JsonObject body = new()
				{
					["cardPackItemIds"] = cardpacksToOpen
				};

				JsonArray resultItemData = [];
				foreach (var cardpackId in cardpacksToOpen)
				{
					var resultNotification = (await account.GetProfile(FnProfileTypes.AccountItems).PerformOperation("OpenCardPack", new JsonObject() { ["cardPackItemId"] = cardpackId })).FirstOrDefault();
					//record in Llamalytics
					resultItemData = [.. resultNotification["lootGranted"]["items"].AsArray().SafeDeepClone()];
				}

				var resultItems = resultItemData
					.Where(val => val?["itemGuid"] is not null && !(val["itemType"]?.ToString().StartsWith("CardPack") ?? false))
					.Select(val => GameItemTemplate.Get(val["itemType"].ToString())?.CreateInstance(
						(int)val["quantity"],
						val["attributes"]?.SafeDeepClone().AsObject(),
						account.GetProfile(val["itemProfile"].ToString()).GetItem(val["itemGuid"].ToString())
						))
					.ToArray();

				var resultCardPacks = resultItemData
					.Where(val => val.AsObject().ContainsKey("itemGuid") && (val["itemType"]?.ToString().StartsWith("CardPack") ?? false))
					.Select(val => account.GetProfile(val["itemProfile"].ToString()).GetItem(val["itemGuid"].ToString()))
					.ToArray();

				GD.Print("LlamaResult: \n" + resultItemData.ToString().FixNewlines());

				var exceptions = resultItemData
					.Where(val => !val.AsObject().ContainsKey("itemGuid"))
					.ToList();
				if (exceptions.Where(i => i["itemType"]?.ToString().StartsWith("Accolades:")==true)?.ToArray() is JsonNode[] accoladeNodes)
				{
					exceptions.RemoveAll(i => i["itemType"]?.ToString().StartsWith("Accolades:") == true);
					var total = accoladeNodes.Sum(i => i["quantity"]?.GetValue<int>() ?? 0);
					GD.Print("Accolade XP: " + total);
				}
				if (exceptions.Count > 0)
					GD.Print("Exceptions: " + string.Join(",", exceptions));

				queuedChoices.AddRange(resultCardPacks);
				queuedItems.AddRange(resultItems);
			}

			queuedChoices.AddRange(extraCardPacks);
			queuedItems.AddRange(extraItems);
			cardPacksPrepared = true;
			singleChoiceMode = queuedChoices.Count == 1 && queuedItems.Count == 0;

			if (singleChoiceMode)
			{
				await WaitForCardPackBurstStart();
				await Helpers.WaitForTimer(0.2);
				StartSingleChoice();
				return;
			}

			//step 2.5: wait for user to proceed
			await WaitForCardPackBurst();
			//GD.Print("wait complete");

			if (!IsInsideTree() || !isOpen)
				return;
			//GD.Print("phew");


			//step 3: apply sorting
			if (sortByRarity)
			{
				var orderedChoices = queuedChoices.OrderBy(item => item.template.RarityLevel);
				queuedChoices = [.. orderedChoices];

				var orderedItems = queuedItems.OrderBy(item => item.template.RarityLevel);
				queuedItems = [.. orderedItems];
			}

			//step 4: display results based on user settings

			choicesOnly = skipReveal;
			if (skipReveal && queuedChoices.Count == 0)
			{
				await ShowRecyclePopup();
				CloseMenu();
				return;
			}

			nextPullIndex = 0;
			SetCardItems(-1);
			smallCardParent.Visible = true;
			var cardsUnfold = GetTree().CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quart);
			cardsUnfold.TweenProperty(this, "CurrentCardSeparation", gapBetweenCards, 0.25f);

			pullButton.Visible = true;
			skipAllButton.Visible = true;
		}
		catch (Exception ex)
		{
			GD.PushWarning(ex);
			await GenericConfirmationWindow.ShowConfirmation("Oops", "", "Close", "Unknown Error while opening Llamas, check the Log for details", ex.Message, allowCancel: false);
			stacheLoadingToken?.Dispose();
			CloseMenu();
		}

	}

	int CurrentCardSeparation
	{
		get => smallCardParent.GetThemeConstant("separation") + Mathf.FloorToInt(topCard.Size.Y);
		set => smallCardParent.AddThemeConstantOverride("separation", value - Mathf.FloorToInt(topCard.Size.Y));
	}

	void ResizePanelOpen()
	{
		if(fromPanel?.GetGlobalRect() is { } startingLocation)
		{
			displayPanel.GlobalPosition = startingLocation.Position;
			displayPanel.Size = startingLocation.Size;
		}
		else
		{
			displayPanel.Position = displayPanel.GetParent<Control>().Size / 2;
			displayPanel.Size = Vector2.Zero;
		}
		displayPanel.Modulate = Colors.Transparent;

		MusicController.StopMusic();
		topCard.Scale = Vector2.Zero;
		music.Play();
		music.VolumeDb = 0;
		greetingEffects[llamaTier].Play();
		UISounds.PlaySound("PanelAppear");

		smallCardParent.Visible = false;

		var resizePanelTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad);
		resizePanelTween.TweenProperty(displayPanel, "modulate", Colors.White, 0.1);
		resizePanelTween.TweenProperty(displayPanel, "offset_top", -10, 0.2).SetDelay(0.1);
		resizePanelTween.TweenProperty(displayPanel, "offset_bottom", 10, 0.2).SetDelay(0.1);
		resizePanelTween.TweenProperty(displayPanel, "offset_left", -10, 0.2).SetDelay(0.1);
		resizePanelTween.TweenProperty(displayPanel, "offset_right", 10, 0.2).SetDelay(0.1);

		resizePanelTween.Finished += () =>
		{
			greetingVoices[isSmall ? 3 : llamaTier].Play();
			standardLlamaButton.Visible = true;
			smallCardParent.Visible = true;
			CurrentCardSeparation = 0;
		};
	}

	//wibbly wobbly music theory
	static readonly float[] impactProgression =
	[
		0.00f/8,
		1.00f/8,
		2.00f/8,
		2.66f/8,
		4.00f/8,
		5.66f/8,
		7.00f/8,
	];

	void PlayImpactSound()
	{
		int octave = 1 + (llamaHits / impactProgression.Length);
		impactEffect.PitchScale = octave + impactProgression[llamaHits % impactProgression.Length];
		impactEffect.Play();
	}

	void SetLlamaHover(bool value)
	{
		if (cardPacksPrepared && llamaHits > minLlamaImpacts)
			return;
		if (value)
			hoverEffect.Play();
		LlamaScale(value);
		GlowScale(value);
	}

	void GlowScale(bool value)
	{
		var glowScaleTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic);
		glowScaleTween.TweenProperty(glowFlare, "scale", value ? Vector2.One : Vector2.Zero, 0.25f);
	}
	void LlamaScale(bool value)
	{
		var llamaScaleTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic);
		llamaScaleTween.TweenProperty(fullLlama, "scale", Vector2.One * (value ? 1 : 0.9f), 0.25f);
	}

	void StacheLlama()
	{
		if (!stacheButton.Visible)
			return;
		shouldStacheLlamas = true;
		waitForFirstHit = false;
	}

	void HitLlama()
	{
		waitForFirstHit = false;
		if (!cardPacksPrepared || llamaHits < minLlamaImpacts)
		{
			//play impact sound and voiceline
			if (llamaHits == 0)
			{
				greetingVoices[isSmall ? 3 : llamaTier].Stop();
				(isSmall ? miniImpactVoice : impactVoice).Play();
			}
			int impactPartcilesIndex = llamaHits % impactParticles.Length;

			impactParticleContainers[impactPartcilesIndex].GlobalPosition = GetGlobalMousePosition();
			impactParticles[impactPartcilesIndex].Restart();

			PlayImpactSound();
			llamaHits++;
			return;
		}
		GlowScale(false);

		standardLlamaButton.Visible = false;
		PlayImpactSound();
		burstEffect.Play();
		(isSmall ? miniImpactVoice : impactVoice).Stop();
		(isSmall ? miniBurstVoice : burstVoice).Play();

		Control[] llamaParts = isSmall ? smallLlamaParts : standardLlamaParts;
		//crateOpenButton.Visible = false;

		fullLlama.Visible = false;
		var llamaPartsParent = isSmall ? smallLlamaPartParent : standardLlamaPartParent;
		llamaPartsParent.Visible = true;

		var llamaBurstTween = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic);
		foreach (var part in llamaParts)
		{
			part.OffsetTop = 0;
			part.OffsetBottom = 0;
			part.OffsetLeft = 0;
			part.OffsetRight = 0;
			part.Rotation = 0;
			part.Scale = Vector2.One;
			float hOffset = ((part.PivotOffset.X / part.Size.X) - 0.5f) * 500;
			llamaBurstTween.TweenProperty(part, "offset_top", 1000, 1.25f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
			llamaBurstTween.TweenProperty(part, "offset_left", hOffset, 1.25f).SetTrans(Tween.TransitionType.Linear);
			llamaBurstTween.TweenProperty(part, "offset_right", hOffset, 1.25f).SetTrans(Tween.TransitionType.Linear);
			llamaBurstTween.TweenProperty(part, "rotation", Mathf.DegToRad(GD.RandRange(480, 740) * (GD.Randf() > 0.5 ? 1 : -1)), 1.25f).SetEase(Tween.EaseType.Out);
			llamaBurstTween.TweenProperty(part, "scale", Vector2.Zero, 1.25f).SetEase(Tween.EaseType.In);
		}

		confettiParticles.Restart();

		llamaBurstTween.Finished += () =>
		{
			llamaBurstComplate = true;
		};
	}

	async Task WaitForCardPackBurstStart()
	{
		while (IsInsideTree() && isOpen && fullLlama.Visible)
		{
			await Helpers.WaitForFrame();
		}
	}

	async Task WaitForCardPackBurst()
	{
		while (IsInsideTree() && isOpen && !llamaBurstComplate)
		{
			await Helpers.WaitForFrame();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!isOpen || @event is not InputEventKey keyEvent)
			return;
		if (pullButton.Visible && !keyEvent.IsEcho() && keyEvent.Keycode == Key.Space)
		{
			if (keyEvent.Pressed)
				StartPullCard();
			else
				EndPullCard();
		}
		if (skipAllButton.Visible && !keyEvent.IsEcho() && keyEvent.Keycode == Key.Escape && keyEvent.Pressed)
		{
			EndImmediate();
		}
	}

	Tween holdTween;
	Tween speedTween;
	public void StartPullCard()
	{
		holdTween = GetTree().CreateTween();
		holdTween.TweenInterval(pullHoldTime);
		holdTween.Finished += () =>
		{
			isFast = true;
			if (!isPulling)
				PullCard();

			//enable fast effects
			if (nextPullIndex < TotalQueueLength)
				TweenTimeScale(pullFastSpeed, pullFastSpeed * 2);
		};
		if (!isPulling)
			PullCard();
	}
	static readonly Callable setTimeScaleCallable = Callable.From<float>(SetTimeScale);
	static void SetTimeScale(float newVal) => Engine.TimeScale = newVal;
	void TweenTimeScale(float target, float pitch, float time = 0.5f)
	{
		speedTween?.Kill();
		speedTween = GetTree().CreateTween().SetParallel();
		speedTween.Pause();
		speedTween.TweenMethod(setTimeScaleCallable, Engine.TimeScale, target, time);
		speedTween.TweenProperty(music, "pitch_scale", pitch, time).SetTrans(Tween.TransitionType.Linear);
	}

	public void EndPullCard()
	{
		if (holdTween?.IsRunning() ?? false)
		{
			holdTween.Stop();
		}
		if (isFast)
		{
			isFast = false;
			TweenTimeScale(1, 1);
			//disable fast effects
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (speedTween?.IsValid() ?? false && Engine.TimeScale > 0)
		{
			var unscaledDelta = delta / Engine.TimeScale;
			speedTween.CustomStep(unscaledDelta);
		}
	}

	void SetCardItems(int index)
	{
		if (index > 0)
		{
			//prevCard.SetItemData(new(queuedItems[index-1].GetItemUnsafe()));
			//prevCard.LinkProfileItem(queuedChoices[index - 1]);
			SetSingleCardItem(index - 1, prevCard);
		}
		if (index >= 0 && index < TotalQueueLength)
		{
			//topCard.SetItemData(new(queuedItems[index].GetItemUnsafe()));
			//topCard.LinkProfileItem(queuedChoices[index]);
			SetSingleCardItem(index, topCard);
		}
		int remainder = Mathf.Max(0, TotalQueueLength - (index + 1));
		int cardCount = Mathf.Min(smallCards.Length, remainder);
		for (int i = 0; i < cardCount; i++)
		{
			//smallCards[i].SetItemData(new(queuedItems[index + i + 1].GetItemUnsafe()));
			//smallCards[i].LinkProfileItem(queuedChoices[index + i + 1]);
			SetSingleCardItem(index + i + 1, smallCards[i]);
			smallCards[i].Modulate = Colors.White;
		}
		for (int i = cardCount; i < smallCards.Length; i++)
		{
			smallCards[i].Modulate = Colors.Transparent;
		}
	}

	void SetSingleCardItem(int index, GameItemEntry card)
	{
		if (choicesOnly)
		{
			card.SetItem(queuedChoices[index]);
			return;
		}
		//GD.Print("INDEX: " + index);
		if (index >= queuedItems.Count)
		{
			index -= queuedItems.Count;
			//choice card
			card.SetItem(queuedChoices[index]);
		}
		else
		{
			//regular item
			card.SetItem(queuedItems[index]);
		}
	}

	void PullCard()
	{
		if (nextPullIndex > 0)
		{
			prevCard.Scale = topCard.Scale;
			prevCard.GlobalPosition = topCard.GlobalPosition/* + (topCard.Size * (topCard.Scale - Vector2.One) * 0.5f)*/;
			prevCard.Rotation = 0;

			prevCard.FixControlOffsets();
		}

		if (nextPullIndex < TotalQueueLength)
		{
			topCard.Scale = smallCards[0].Scale;
			topCard.GlobalPosition = smallCards[0].GlobalPosition;
			topCard.Rotation = 0;

			topCard.FixControlOffsets();

			smallCardsOffset.Position += new Vector2(0, gapBetweenCards);
		}
		else
		{
			GlowScale(false);
			topCard.Scale = Vector2.Zero;
			pullButton.Visible = false;
			skipAllButton.Visible = false;
			EndPullCard();
		}

		SetCardItems(nextPullIndex);

		bool pauseForChoice = false;
		//if current item is cardpack, add delay in fast mode or stop fast mode
		if (nextPullIndex < TotalQueueLength && (nextPullIndex >= queuedItems.Count || choicesOnly))
		{
			EndPullCard();
			pauseForChoice = true;
		}

		nextPullIndex++;

		isPulling = true;


		var tweener = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quart);
		float delay = 0;

		if (nextPullIndex <= TotalQueueLength)
		{
			tweener.TweenProperty(topCard, "offset_top", 0, pullTime)
				.SetEase(Tween.EaseType.Out);
			tweener.TweenProperty(topCard, "offset_bottom", 0, pullTime)
				.SetEase(Tween.EaseType.Out);
			tweener.TweenProperty(topCard, "offset_left", 0, pullTime)
				.SetEase(Tween.EaseType.In);
			tweener.TweenProperty(topCard, "offset_right", 0, pullTime)
				.SetEase(Tween.EaseType.In);

			tweener.TweenProperty(topCard, "rotation", Mathf.DegToRad(-360), pullTime * 0.5f)
				.SetEase(Tween.EaseType.Out)
				.SetTrans(Tween.TransitionType.Quad);
			tweener.TweenProperty(topCard, "scale", Vector2.One * 1.5f, pullTime)
				.SetEase(Tween.EaseType.In);

			tweener.TweenProperty(smallCardsOffset, "position:y", 0, pullTime);
			tweener.TweenProperty(glowFlare, "self_modulate", topCard.currentItem.template.RarityColor, pullTime);
			delay = pullTime * 0.75f;
		}


		if (nextPullIndex > 0)
		{
			tweener.TweenProperty(prevCard, "offset_right", -450, pullTime * 0.5f)
				.SetEase(Tween.EaseType.Out)
				.SetTrans(Tween.TransitionType.Quart)
				.SetDelay(delay);
			tweener.TweenProperty(prevCard, "offset_top", 1000, pullTime * 0.5f)
				.SetEase(Tween.EaseType.In)
				.SetTrans(Tween.TransitionType.Quad)
				.SetDelay(delay);

			tweener.TweenProperty(prevCard, "scale", Vector2.Zero, pullTime * 0.5f)
				.SetEase(Tween.EaseType.In)
				.SetTrans(Tween.TransitionType.Quad)
				.SetDelay(delay);
			tweener.TweenProperty(prevCard, "rotation", Mathf.DegToRad(-720), pullTime * 0.5f)
				.SetEase(Tween.EaseType.In)
				.SetTrans(Tween.TransitionType.Quad)
				.SetDelay(delay);
		}

		tweener.Finished += async () =>
		{
			if (nextPullIndex > TotalQueueLength)
			{
				if (speedTween?.IsValid() ?? false)
					await ToSignal(speedTween, "finished");
				EndPullCard();
				TweenTimeScale(1, 1, 0.1f);
				await ToSignal(speedTween, "finished");

				await ShowRecyclePopup();
				CloseMenu();
				return;
			}
			GlowScale(true);
			if (pauseForChoice)
			{
				//open choice panel
				GD.Print("opening choice");
				isPulling = false;
				pullButton.Visible = false;
				OpenChoices();
				return;
			}
			if (isFast)
			{
				PullCard();
			}
			else
				isPulling = false;
		};

	}

	async void StartSingleChoice()
	{
		topCard.Scale = Vector2.Zero;
		topCard.ResetOffsets();
		topCard.Rotation = 0;
		topCard.SetItem(queuedChoices[0]);
		var tweener = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quart);
		tweener.TweenProperty(topCard, "scale", Vector2.One*1.5f, pullTime)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		tweener.Finished += () =>
		{
			GlowScale(true);
			OpenChoices();
		};
	}

	bool isChosing = false;
	async void OpenChoices()
	{
		isChosing = true;
		if (singleChoiceMode)
			cancelChoiceButton.Visible = true;
		else
			skipChoiceButton.Visible = true;

		int nextChoiceIndex = choicesOnly ? nextPullIndex - 1 : nextPullIndex - (queuedItems.Count + 1);
		GD.Print(nextChoiceIndex);
		nextChoiceIndex = Mathf.Clamp(nextChoiceIndex, 0, queuedChoices.Count - 1);
		GD.Print(nextChoiceIndex);
		GD.Print(queuedChoices[nextChoiceIndex]);
		JsonArray currentChoices = null;
		if (queuedChoices[nextChoiceIndex].profile is not null && queuedChoices[nextChoiceIndex].attributes["options"]?.AsArray() is JsonArray choices)
			currentChoices = choices;
		if (currentChoices is null)
		{
			SkipChoice(false);
			return;
		}
		List<Task> cardAnims = [];
		for (int i = currentChoices.Count; i < choiceCardEntries.Length; i++)
		{
			choiceCardEntries[i].Visible = false;
		}
		for (int i = 0; i < Mathf.Min(currentChoices.Count, choiceCardEntries.Length); i++)
		{
			var thisChoice = currentChoices[i];
			var choiceTemplate = GameItemTemplate.Get(thisChoice["itemType"].ToString());
			var choiceItem = choiceTemplate.CreateInstance(thisChoice["quantity"].GetValue<int>(), thisChoice["attributes"]?.AsObject().SafeDeepClone());
			choiceCardEntries[i].SetItem(choiceItem);
			choiceCardEntries[i].SetInteractable(false);
			choiceCardEntries[i].Visible = true;
			choiceCardEntries[i].Modulate = Colors.White;
			cardAnims.Add(ChoiceCardIntroAnim(i));
			await Helpers.WaitForTimer(0.1);
		}
		await Task.WhenAll(cardAnims);
		for (int i = 0; i < choiceCardEntries.Length; i++)
		{
			choiceCardEntries[i].SetInteractable(true);
		}
	}

	const float stepAmt = 200;
	async Task ChoiceCardIntroAnim(int index)
	{
		bool left = index % 2 == 0;
		int step = index / 2;

		choiceCardEntries[index].OffsetTransformPosition = Vector2.Zero;
		choiceCardEntries[index].OffsetTransformRotation = Mathf.DegToRad((left ? -5 : 5) * (step+1));
		choiceCardEntries[index].OffsetTransformScale = Vector2.Zero;
		choiceCardEntries[index].FlipProgress = 1;
		choiceCardEntries[index].BurnProgress = 0;
		choiceCardEntries[index].LabelOpacity = 0;
		choiceCardEntries[index].ZIndex = -1;

		var choicePullOut = GetTree().CreateTween().SetParallel();
		var resultPos = stepAmt * 0.5f + stepAmt * step;
		choicePullOut.TweenProperty(choiceCardEntries[index], "offset_transform_position", Vector2.Left * (resultPos + stepAmt * 0.5f) * (left ? 1 : -1), 0.2).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Circ);
		choicePullOut.TweenProperty(choiceCardEntries[index], "offset_transform_position", Vector2.Left * resultPos * (left ? 1 : -1), 0.2).SetDelay(0.2).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Circ);
		choicePullOut.TweenProperty(choiceCardEntries[index], "offset_transform_scale", Vector2.One, 0.4);
		choicePullOut.TweenProperty(choiceCardEntries[index], "FlipProgress", 0, 0.2).SetDelay(0.45);
		choicePullOut.TweenProperty(choiceCardEntries[index], "LabelOpacity", 1, 0.1).SetDelay(0.55);
		await Helpers.WaitForTimer(0.2);
		choiceCardEntries[index].ZIndex = 0;
		await Helpers.WaitForTimer(0.45);
	}

	async void ApplyChoice(int index)
	{
		try
		{
			if (!isChosing)
				return;
			isChosing = false;

			skipChoiceButton.Visible = false;
			cancelChoiceButton.Visible = false;

			int nextChoiceIndex = choicesOnly ? nextPullIndex - 1 : nextPullIndex - (queuedItems.Count + 1);
			//start the request now and await later, asynchronism baby!
			JsonObject body = new()
			{
				["cardPackItemId"] = queuedChoices[nextChoiceIndex].uuid,
				["selectionIdx"] = index
			};
			var operationTask = GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("OpenCardPack", body.ToString());
			//var operationTask = Task.FromResult<JsonArray>(null);
			for (int i = 0; i < choiceCardEntries.Length; i++)
			{
				choiceCardEntries[i].SetInteractable(false);
			}

			var currentChoices = queuedChoices[nextChoiceIndex].attributes["options"].AsArray();
			var resultChoiceData = currentChoices[index];
			var itemTemplate = GameItemTemplate.Get(resultChoiceData["itemType"].ToString());
			var itemInstance = itemTemplate.CreateInstance((int?)resultChoiceData["quantity"] ?? 1, resultChoiceData["attributes"]?.AsObject().SafeDeepClone() ?? null);

			var resultTarget = choiceCardEntries[index];
			var discardTargets = choiceCardEntries.Except([resultTarget]).ToArray();

			//choiceResultShader.Visible = true;
			//choiceResultShader.Reparent(choiceResultFGParent);
			//choiceResultShader.Scale = Vector2.One;
			//choiceResultShader.GlobalPosition = resultEntry.GlobalPosition;
			//choiceResultShader.OffsetLeft += choiceResultShader.Size.X * 0.5f;
			//choiceResultShader.OffsetRight = choiceResultShader.OffsetLeft;
			//choiceResultShader.OffsetTop += choiceResultShader.Size.Y * 0.5f;
			//choiceResultShader.OffsetBottom = choiceResultShader.OffsetTop;
			//choiceResultShader.SetShaderTexture(itemInstance.GetTexture(), "IconTexture");
			//choiceResultShader.SetShaderColor(itemTemplate.RarityColor, "RarityColour");
			//choiceResultShader.SetShaderBool(false, "Started");


			//var choiceClose = GetTree().CreateTween().SetParallel();
			//choiceClose.TweenProperty(choiceCanvas, "self_modulate", Colors.Transparent, 0.25);
			//choiceClose.TweenProperty(choiceCanvas, "scale", Vector2.Zero, 0.25).SetEase(Tween.EaseType.In);

			if (discardTargets.Length > 0)
			{
				var cardBurn = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad);
				foreach (var discard in discardTargets)
				{
					cardBurn.TweenProperty(discard, "offset_transform_position", discard.OffsetTransformPosition + Vector2.Down * Mathf.Abs(discard.OffsetTransformPosition.X * 0.5f), 1);
					cardBurn.TweenProperty(discard, "offset_transform_rotation", Mathf.DegToRad((discard.OffsetTransformPosition.X > 0 ? 15 : -15) + (discard.OffsetTransformPosition.X * 0.1)), 1);
					cardBurn.TweenProperty(discard, "BurnProgress", 1, 1).SetTrans(Tween.TransitionType.Linear);
					cardBurn.TweenProperty(discard, "LabelOpacity", 0, 0.1);
				}
				cardBurn.Finished += () =>
				{
					foreach (var discard in discardTargets)
						discard.Visible = false;
				};
			}

			var cardSlideUp = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad);
			var relOffset = resultDestination.Position - resultTarget.Position;
			cardSlideUp.TweenProperty(resultTarget, "offset_transform_position", relOffset, 0.3);
			cardSlideUp.TweenProperty(resultTarget, "offset_transform_rotation", 0, 0.1);
			cardSlideUp.TweenProperty(resultTarget, "offset_transform_scale", Vector2.One * 0.5f, 0.3);
			cardSlideUp.TweenProperty(resultTarget, "FlipProgress", 1, 0.15).SetDelay(0.15).SetTrans(Tween.TransitionType.Linear);
			cardSlideUp.TweenProperty(resultTarget, "LabelOpacity", 0, 0.05);

			//GD.Print(choiceResultCard.GetShaderFloat("time"));
			//GD.Print(choiceResultCard.GetShaderFloat("StartTime"));

			await Helpers.WaitForTimer(0.4f);

			JsonObject resultNotification = null;
			try
			{
				resultNotification = (await operationTask)[0].AsObject();
			}
			catch
			{
				await GenericConfirmationWindow.ShowError("Failed to make choice");
			}
			resultTarget.ZIndex = -1;


			var cardSlideDown = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
			cardSlideDown.TweenProperty(resultTarget, "offset_transform_position", resultTarget.OffsetTransformPosition + Vector2.Down * 115, 0.25);

			await Helpers.WaitForTimer(0.2f);
			cardChangeEffect?.Visible = true;
			CardChangeEffectLevel = 0;
			var changeEffectTween = GetTree().CreateTween();
			changeEffectTween.TweenProperty(this, "CardChangeEffectLevel", 1, 1);
			changeEffectTween.TweenProperty(this, "CardChangeEffectLevel", 2, 1);

			await Helpers.WaitForTimer(1f);
			resultTarget.Visible = false;

			GameItem resultItem = null;
			if (resultNotification is not null)
			{
				JsonNode resultItemData = resultNotification["lootGranted"]["items"][0];
				resultItem = GameAccount.ActiveAccount.GetProfile(resultItemData["itemProfile"].ToString()).GetItem(resultItemData["itemGuid"].ToString());
				topCard.SetItem(resultItem);
				queuedChoices[nextChoiceIndex] = resultItem;
			}

			await Helpers.WaitForTimer(1f);
			cardChangeEffect?.Visible = true;

			//reopen choices if the result is another cardpack
			if (resultItem?.template.Type == "CardPack")
				OpenChoices();
			else if (singleChoiceMode)
				singleChoiceEndContent.Visible = true;
			else
				pullButton.Visible = true;
		}
		catch
		{
			//if anything weird goes wrong, try to recover, but still throw
			if (singleChoiceMode)
				singleChoiceEndContent.Visible = true;
			else
				pullButton.Visible = true;
			for (int i = 0; i < choiceCardEntries.Length; i++)
			{
				choiceCardEntries[i].Visible = false;
			}
			throw;
		}
	}

	float CardChangeEffectLevel
	{
		get => cardChangeEffect?.GetShaderFloat("progress") ?? 0;
		set => cardChangeEffect?.SetShaderFloat(value, "progress");
	}

	void SkipChoice() => SkipChoice(true);
	void SkipChoice(bool withContinue)
	{
		if (!isChosing)
			return;
		isChosing = false;

		var choiceClose = GetTree().CreateTween().SetParallel().SetEase(Tween.EaseType.In);
		foreach (var choice in choiceCardEntries)
		{
			choice.SetInteractable(false);
			int dir = choice.OffsetTransformPosition.X > 0 ? 1 : -1;
			choiceClose.TweenProperty(choice, "offset_transform_position:x", choice.OffsetTransformPosition.X + 75 * dir, 0.2).SetEase(Tween.EaseType.Out);
			choiceClose.TweenProperty(choice, "offset_transform_position:y", 150, 0.2).SetTrans(Tween.TransitionType.Back);
			choiceClose.TweenProperty(choice, "offset_transform_rotation", Mathf.DegToRad(10 * dir), 0.2);
			choiceClose.TweenProperty(choice, "modulate", Colors.Transparent, 0.2);
		}

		skipChoiceButton.Visible = false;
		cancelChoiceButton.Visible = false;
		if (singleChoiceMode)
		{
			choiceClose.Finished += () => CloseMenu();
		}
		else
		{
			pullButton.Visible = true;
			if (withContinue)
				PullCard();
		}
	}

	async void RecycleSingleChoiceAndEnd()
	{
		if (queuedChoices[0].template?.Unrecyclable == false)
		{
			JsonObject content = new()
			{
				["targetItemIds"] = new JsonArray([queuedChoices[0].uuid])
			};
			GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", content).StartTask();
		}
		CloseMenu();
	}

	async Task ShowRecyclePopup()
	{
		var resultItems = queuedChoices
					.Union(queuedItems)
					.Select(item => item.template.IsCollectable ? (item.inspectorOverride ?? item) : item)
					.ToArray();

		if (queuedChoices.Count == 1 && queuedItems.Count == 0)
			return;

		if (resultItems.Length>0)
		{
			foreach (var item in resultItems)
			{
				item.GetSearchTags();
				item.GenerateRawData();
			}
			var toRecycle = await SimpleItemSelector.OpenMultiSelector(resultItems, SimpleItemSelector.RecycleConfig with
			{
				allowCancel = false,
				allowEmptySelection = true,
				unselectableMarkerTex = null,
				unselectableTintColor = Colors.Transparent,
			});
			var recycleIds = toRecycle.Select(item => (JsonNode)item.uuid).Where(id => id is not null).ToArray();
			if (toRecycle.Length > 0)
			{
				JsonObject content = new()
				{
					["targetItemIds"] = new JsonArray(recycleIds)
				};
				GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).PerformOperation("RecycleItemBatch", content).StartTask();
			}
		}
	}

	public async void EndImmediate()
	{
		if (!isOpen)
			return;
		await ShowRecyclePopup();
		CloseMenu();
	}

	async void CloseMenu()
	{
		OnLlamaOpeningComplete?.Invoke();
		MusicController.ResumeMusic();
		var exitAnim = GetTree().CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad);
		//bgFade.TweenProperty(backgroundImage, "self_modulate", Colors.Transparent, 0.25f);
		if (fromPanel?.GetGlobalRect() is { } startingLocation)
		{
			exitAnim.TweenProperty(displayPanel, "global_position", startingLocation.Position, 0.2);
			exitAnim.TweenProperty(displayPanel, "size", startingLocation.Size, 0.2);
		}
		else
		{
			exitAnim.TweenProperty(displayPanel, "position", displayPanel.GetParent<Control>().Size / 2, 0.2);
			exitAnim.TweenProperty(displayPanel, "size", Vector2.Zero, 0.2);
		}
		exitAnim.TweenProperty(displayPanel, "modulate", Colors.Transparent, 0.1).SetDelay(0.2);
		exitAnim.TweenProperty(music, "volume_db", -80, 1)
			.SetTrans(Tween.TransitionType.Expo)
			.SetEase(Tween.EaseType.In);

		await Helpers.WaitForTimer(0.31f);
		queuedChoices.Clear();
		queuedItems.Clear();
		displayPanel.Visible = false;
		isPulling = false;
		isOpen = false;
		ProcessMode = ProcessModeEnum.Disabled;
	}
}
