shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/FlipbookSlice.gdshaderinc"
#include "Includes/ShadowEffect.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 SliceAmount = vec2(4.000000, 4.000000);
uniform float FPS = 60;
uniform int CellCount;
uniform bool Fit;
uniform bool Repeat;
uniform float HorizontalMask;
uniform float VerticalMask;
uniform int Frame = -1;
uniform bool scriptTime;
uniform float time = 0;
uniform bool UseShadow;
uniform float shadowBrightness=0.0;
uniform float shadowOpacity=1.0;

varying vec4 modulate;

void vertex() {
	modulate=COLOR;
}

void fragment() {
	float hmask = HorizontalMask>0.0 ? min(UV.x/HorizontalMask, -(UV.x/HorizontalMask)+(1.0/HorizontalMask)) : 1.0;
	float vmask = VerticalMask>0.0 ? min(UV.y/VerticalMask, -(UV.y/VerticalMask)+(1.0/VerticalMask)) : 1.0;
	float totalMask = min(hmask, vmask);

	float ratio=SliceAmount.x/SliceAmount.y;
	vec2 ratioScale = ratio>1.0?vec2(1,ratio):vec2(1.0/ratio,1);

	vec2 adaptedUV = Fit ? adaptToAspect(ControlSize, vec2(0.5),ratioScale/TEXTURE_PIXEL_SIZE,UV, true) : UV;
	vec2 slicedUV = flipbookSlice(adaptedUV, SliceAmount, float(CellCount), Frame<0 ? FPS*(scriptTime ? time : TIME) : float(Frame),Repeat);
	COLOR = texture(TEXTURE, slicedUV) * modulate;
	//COLOR = vec4(0,0,0,1);
	//COLOR.rg = slicedUV;
	COLOR.a = COLOR.a * clamp(totalMask,0.0,1.0);
	if(slicedUV.x<-0.5)
		COLOR = vec4(0);
	if(UseShadow)
		COLOR = AddShadow(COLOR, adaptedUV, shadowBrightness, shadowOpacity);
}
