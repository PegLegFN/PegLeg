shader_type canvas_item;
#include "Includes/FlipbookSlice.gdshaderinc"

uniform sampler2D IconTexture;
uniform sampler2D UVTexture;
uniform sampler2D NoiseTexture;
uniform vec4 FireColor : source_color = vec4(1.0,0.5,0.0,1.0);
uniform int ZTileGrid = 0;
uniform bool FlipUV_Y = false;
uniform ivec2 SliceAmount;

uniform bool Reverse;
uniform bool PingPong;

uniform int ManualControl = -1;
uniform float NormalisedManual : hint_range(0.0, 1.0) = 0;
uniform float BurnProgress : hint_range(0.0, 1.0) = 0;

vec2 evalCardUV(vec3 cardUVRGB)
{
	if(ZTileGrid>1)
	{
		float cellUnit = 1.0/float(ZTileGrid);//4=>0.25
		float indexUnit = cellUnit * cellUnit;//4=>0.0625
		
		int index = int(cardUVRGB.z/indexUnit);
		int yGridPos = index/ZTileGrid;
		int xGridPos = index-yGridPos;
		
		return (vec2(float(xGridPos), float(yGridPos)) + cardUVRGB.xy)*cellUnit;
	}
	else
	{
		return cardUVRGB.xy;
	}
}

varying vec4 Modulate;
void vertex()
{
	Modulate=COLOR;
}

void fragment() {
	float index= ManualControl>=0 ? float(ManualControl) : (float((SliceAmount.x*SliceAmount.y)-1)*NormalisedManual);
	
	vec2 slicedUV = flipbookSlice(UV, vec2(SliceAmount), 0.0, index, false);
	vec4 cardTex = texture(TEXTURE, slicedUV);
	vec4 cardUVCol = texture(UVTexture, slicedUV);
	vec2 cardUV = evalCardUV(cardUVCol.rgb);
	if(FlipUV_Y)
		cardUV.y = 1.0-cardUV.y;
	
	vec4 cardContent = texture(IconTexture, cardUV);
	cardContent = mix(vec4(0.0), cardContent, cardContent.a);
	
	cardContent.a *= cardUVCol.a;
	
	COLOR.rgb = mix(cardTex.rgb, cardContent.rgb*cardTex.rgb, cardContent.a);
	COLOR.a = cardTex.a;
	
	float burnNoise = texture(NoiseTexture,UV).r;
	burnNoise*=-(distance(UV, vec2(0.5))*3.0)+0.9;
	burnNoise-=1.0;
	burnNoise+=BurnProgress*2.0;
	
	float threshold = 0.05;
	burnNoise-=threshold+0.1;
	float burnInvis = burnNoise*(1.0-threshold);
	float burnFire = burnInvis+threshold;
	
	burnInvis = clamp(burnInvis*20.0,0,1);
	burnFire = clamp(burnFire*20.0,0,1);
	
	COLOR.rgb = mix(COLOR.rgb, FireColor.rgb, burnFire);
	COLOR.a = mix(COLOR.a, 0, burnInvis);
	COLOR *= Modulate;
}

//void light() {
//	// Called for every pixel for every light affecting the CanvasItem.
//	// Uncomment to replace the default light processing function with this one.
//}
