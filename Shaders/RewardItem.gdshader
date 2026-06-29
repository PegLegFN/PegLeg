shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"
#include "Includes/CardPackIcon.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float CornerRadius = 5.0;
uniform bool Fit = true;
uniform bool IsHero;
uniform bool IsSchematic;
uniform bool IsFancy;
uniform bool IsPack;
uniform bool IsChoice;
uniform sampler2D MainIcon: hint_default_transparent;
uniform sampler2D MainPackIcon: hint_default_transparent;
uniform vec4 AltRarity : source_color;
uniform bool AltHero;
uniform bool AltSchematic;
uniform bool AltFancy;
uniform bool AltPack;
uniform sampler2D AltIcon : hint_default_transparent;
uniform sampler2D AltPackIcon: hint_default_transparent;
uniform bool HeroBeamsOnly;
uniform float ChoiceShift : hint_range(0.0, 1.0);
uniform bool HideRarity;
uniform bool HideBar;

varying vec4 modulate;
void vertex() {
	modulate = COLOR;
}

float gridFunc(float input, float line, float grid)
{
	return 1.0-clamp(abs(mod(input, grid)-grid*0.5)*(1.0/grid)*line, 0, 1);
}

float radial(vec2 uv, vec2 from)
{
	vec2 src = uv-from;
	return atan(src.x/src.y);
}

vec4 renderItem(vec2 uv, sampler2D tex, vec4 rarity, bool schematic, bool hero, bool fancy, bool pack, sampler2D packTex)
{
	vec4 backgroundCol = rarity;
	float gradient = 1.0-distance(vec2(0.5, 1.25), uv*vec2(1,1.5));
	if(HideRarity)
		rarity = vec4(0);
	if(hero)
	{
		if(HeroBeamsOnly)
		{
			float radialVal = radial(uv, vec2(0.5,1.2));
			float beams = clamp(1.1-floor(abs(radialVal*radialVal)*30.0)/5.0, 0.0, 1);
			backgroundCol = rarity * clamp(beams*(gradient+0.2)*1.5,0.0,1.0);
		}
		else
		{
			float radialVal = radial(uv, vec2(0.5,1.2));
			float beams = clamp(1.1-floor(abs(radialVal)*7.0)/5.0, 0.25, 1);
			backgroundCol = rarity * clamp(beams*(gradient+0.2)*1.5,0.0,1.0);
		}
	}
	else if(schematic)
	{
		float xGrid = gridFunc(ControlSize.x*uv.x-0.5, 9, 9);
		float yGrid = gridFunc(ControlSize.y*uv.y, 9, 9);
		float fullGrid = min(xGrid+yGrid, 1);

		backgroundCol = rarity * clamp((gradient*0.8)+(fullGrid*0.5*(gradient+0.4)),0.0,1.0);
		//backgroundCol= vec4(xGrid);
	}
	else if(fancy)
	{
		//replace with diagonal gradient glow
		backgroundCol = rarity * clamp(gradient,0.0,1.0);
	}
	else
	{
		backgroundCol = rarity * clamp(gradient,0.0,1.0);
	}
	vec4 fitTex = texture(tex, uv);
	if (pack) {
		fitTex = MixPackIcon(fitTex, uv, packTex);
		//vec2 packUV=(uv-vec2(0.336,0.54))*7.5;
		//packUV.y+=packUV.x*-0.05;
		//packUV.y*=1.0-(packUV.x*0.1);
		//vec4 packCol=texture(packTex, packUV);
		//fitTex.rgb=mix(fitTex.rgb,vec3(0.2890625,0.2578125,0.2265625),packCol.r*packCol.a);
	}
	return mix(backgroundCol, fitTex, fitTex.a);
}

const float choiceGap = 0.2;
const float choiceDist = 1.0+choiceGap;
void fragment() {
	vec2 fitUV = adaptToAspect(ControlSize, vec2(0.5, 1), 1.0/TEXTURE_PIXEL_SIZE, UV, Fit);
	vec2 controlSpace = UV*ControlSize;
	vec4 barColor = modulate;
	if(HideRarity)
		barColor = vec4(0);

	if(IsChoice)
	{
		vec2 shiftedUV = fitUV+vec2(ChoiceShift*choiceDist,0.0);

		vec4 first = renderItem(shiftedUV, MainIcon, modulate, IsSchematic, IsHero, IsFancy, IsPack, MainPackIcon);
		vec4 second = renderItem(shiftedUV+vec2(-choiceDist,0.0), AltIcon, AltRarity, AltSchematic, AltHero, AltFancy, AltPack, AltPackIcon);

		float gradient = clamp((fitUV.x+(ChoiceShift*choiceDist)-1.0)/choiceGap, 0, 1);
		COLOR = mix(first, second, gradient);
		barColor = mix(modulate, AltRarity, gradient);
		//COLOR.rgb=vec3(gradient);
		//COLOR.a=1.0;
	}
	else
	{
		COLOR = renderItem(fitUV, MainIcon, modulate, IsSchematic, IsHero, IsFancy, IsPack, MainPackIcon);
	}

	if(controlSpace.y>(ControlSize.y-CornerRadius) && !HideBar)
	{
		COLOR = mix(COLOR, barColor, modulate.a);
		COLOR.a *= cornerMask(UV, CornerRadius, ControlSize);
	}
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
