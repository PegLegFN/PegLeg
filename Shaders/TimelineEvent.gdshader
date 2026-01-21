shader_type canvas_item;
#include "Includes/AdaptToAspect.gdshaderinc"
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 Parallax = vec2(0.0, 0.0);
uniform float CornerRadius = 10.0;
uniform float HeaderSpace = 250;
uniform float SkySpace = 0.4;
uniform float ParallaxScale = 350;
uniform sampler2D background : hint_default_black;

varying vec4 modulate;

void vertex()
{
	modulate=COLOR;
}

void fragment() {
	vec2 gradientUV = adaptToAspect(ControlSize, vec2(0.5,0.5), vec2(2,1),UV,true);
	
	vec2 texRes=vec2(textureSize(background, 0));
	vec2 backgroundUV = adaptToAspect(ControlSize, vec2(0,0.5), texRes,UV-vec2(0.0,0.0),true);
	
	backgroundUV-=SkySpace;
	backgroundUV/=1.0-SkySpace;
	
	//vec2 objUV = (UV-vec2(0.5,0))*ControlSize;
	//objUV/=ControlSize.y*0.5;
	//COLOR = texture(TEXTURE, adaptedUV+vec2((Parallax.x/ControlSize.x), 0.0));
	//float val = ((gradientUV.x+gradientUV.y)*0.1)+0.2;
	//COLOR.rgb = vec3(objUV.x, objUV.y, 0);
	COLOR.rgb = (1.0-UV.y*0.5)*modulate.rgb*0.75;
	
	//COLOR.rgb = vec3(objUV.x, objUV.y, 0);
	//objUV.x = mod(objUV.x, 1);
	//COLOR.rgb = texture(sky, objUV).r*modulate.rgb;
	
	vec2 bgUV = backgroundUV+vec2(Parallax.x/ParallaxScale, 0.0);
	bgUV.x = mod(bgUV.x, 1.0);
	vec4 bg = texture(background, bgUV);
	COLOR.rgb = mix(COLOR.rgb,mix(modulate.rgb*0.5,mix(modulate.rgb,vec3(1),0.3),bg.r),clamp(((bg.r*15.0)-2.0)*bg.a, 0, 1));
	
	float contentGradient = (ControlSize.x*UV.x)/HeaderSpace;
	//COLOR.rgb = mix(COLOR.rgb,0.9*modulate.rgb,clamp(1.5-(contentGradient*1.5), 0, 1));
	
	COLOR.a *= clamp(cornerMask(UV,CornerRadius,ControlSize), 0, 1);
	//COLOR *= modulate;
}