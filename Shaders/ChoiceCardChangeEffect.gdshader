shader_type canvas_item;
uniform float progress : hint_range(0, 2) = 0;
uniform float scalar=100;
uniform sampler2D bgTex;

varying vec4 modulate;
void vertex() {
	modulate=COLOR;
	// Called for every vertex the material is visible on.
}

float GradientCircle(float gradient, float currentThreshold)
{
	gradient-=currentThreshold;
	return clamp(1.0-(gradient*scalar),0,1);
}

void fragment() {
	float gradient = COLOR.r+(1.0/scalar);
	gradient=pow(gradient, 3);
	COLOR.rgb=modulate.rgb;
	float overlayScale=GradientCircle(gradient, progress-0.4);
	COLOR.rgb*=(texture(bgTex, mod((UV+mod(TIME*0.1, 1)*vec2(1,-1))*4.0,1.0)).a*overlayScale) < 0.5 ? vec3(0.5):vec3(1.0);
	COLOR.a = 0.0;
	//COLOR.a+=GradientCircle(gradient, progress-0.1)*0.25;
	COLOR.a+=GradientCircle(gradient, progress-0.2)*0.75;
	//COLOR.a+=GradientCircle(gradient, progress-0.3)*0.25;
	COLOR.a+=overlayScale*0.5;
	COLOR.a*=1.0-GradientCircle(gradient, (progress-1.0)*0.77);
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
