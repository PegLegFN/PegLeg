using Godot;

public partial class ItemTierDisplay : Node
{
	[Export]
	TextureRect[] tierImages;

	[Export]
	Color disabledColor = Colors.Black;

	[Export]
	Color regularColor = Colors.White;

	[Export]
	Color superchargeColor = Colors.Cyan;

	public void SetFromItem(GameItem item)
	{
		SetMaxTier(item.template?.MaxTier ?? 1);
		SetTier(item.template?.Tier ?? 0);
		SetSuperchargedTier((item.attributes?["max_level_bonus"]?.GetValue<int>() ?? 0) / 2);
	}

	public void SetMaxTier(int maxTier)
	{
		maxTier = Mathf.Min(maxTier, tierImages.Length);
		for (int i = 0; i < maxTier; i++)
		{
			tierImages[i].Visible = true;
		}
		for (int i = maxTier; i < tierImages.Length; i++)
		{
			tierImages[i].Visible = false;
		}
	}

	public void SetTier(int tier)
	{
		tier = Mathf.Min(tier, tierImages.Length);
		for (int i = 0; i < tier; i++)
		{
			tierImages[i].SelfModulate = regularColor;
		}
		for (int i = tier; i < tierImages.Length; i++)
		{
			tierImages[i].SelfModulate = disabledColor;
		}
	}

	public void SetSuperchargedTier(int superchargedTier)
	{
		superchargedTier = Mathf.Min(superchargedTier, tierImages.Length);
		for (int i = 0; i < superchargedTier; i++)
		{
			tierImages[i].SelfModulate = superchargeColor;
		}
	}
}
