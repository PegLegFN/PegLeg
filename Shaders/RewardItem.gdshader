shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform float CornerRadius = 5.0;
uniform bool Fit = true;
uniform bool IsHero;
uniform bool IsSchematic;
uniform bool IsFancy;
uniform bool IsChoice;
uniform sampler2D MainIcon;
uniform vec4 AltRarity : source_color;
uniform sampler2D AltIcon;
uniform bool AltSchematic;
uniform bool AltHero;
uniform bool AltFancy;
uniform float ChoiceShift : hint_range(0.0, 1.0);

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

vec4 renderItem(vec2 uv, sampler2D tex, vec4 rarity, bool schematic, bool hero, bool fancy)
{
	vec4 backgroundCol = rarity;
	float gradient = 1.0-distance(vec2(0.5, 1.25), uv*vec2(1,1.5));
	if(hero)
	{
		float radialVal = radial(uv, vec2(0.5,1.2));
		float beams = clamp(1.1-floor(abs(radialVal)*7.0)/5.0, 0.25, 1);
		backgroundCol = rarity * clamp(beams*(gradient+0.2)*1.5,0.0,1.0);
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
	return mix(backgroundCol, fitTex, fitTex.a);
}

const float choiceGap = 0.2;
const float choiceDist = 1.0+choiceGap;
void fragment() {
	vec2 fitUV = adaptToAspect(ControlSize, vec2(0.5, 1), 1.0/TEXTURE_PIXEL_SIZE, UV, Fit);
	vec2 controlSpace = UV*ControlSize;
	vec4 barColor = modulate;
	
	if(IsChoice)
	{
		vec2 shiftedUV = fitUV+vec2(ChoiceShift*choiceDist,0.0);
		
		vec4 first = renderItem(shiftedUV, MainIcon, modulate, IsSchematic, IsHero, IsFancy);
		vec4 second = renderItem(shiftedUV+vec2(-choiceDist,0.0), AltIcon, AltRarity, AltSchematic, AltHero, AltFancy);
		
		float gradient = clamp((fitUV.x+(ChoiceShift*choiceDist)-1.0)/choiceGap, 0, 1);
		COLOR = mix(first, second, gradient);
		barColor = mix(modulate, AltRarity, gradient);
		//COLOR.rgb=vec3(gradient);
		//COLOR.a=1.0;
	}
	else
	{
		COLOR = renderItem(fitUV, MainIcon, modulate, IsSchematic, IsHero, IsFancy);
	}
	
	if(controlSpace.y>(ControlSize.y-CornerRadius))
	{
		COLOR = mix(COLOR, barColor, modulate.a);
		COLOR.a *= cornerMask(UV, CornerRadius, ControlSize);
	}
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
