shader_type canvas_item;
uniform float rotationSpeed = 0.5;

varying vec4 modulate;
void vertex() {
	modulate = COLOR;
	// Called for every vertex the material is visible on.
}

// credit to bemyak on the Godot forum
//https://forum.godotengine.org/t/simple-texture-rotation-shader/24198/3
vec2 rotateUV(vec2 uv, vec2 pivot, float rotation) {
    float cosa = cos(rotation);
	float sina = sin(rotation);
	uv -= pivot;
	return vec2(
		cosa * uv.x - sina * uv.y,
		cosa * uv.y + sina * uv.x 
	) + pivot;
}

float maskUV(vec2 uv){
	return clamp(1.0-(
			max(floor(1.0-uv.x),0)+
			max(floor(1.0-uv.y),0)+
			max(floor(uv.x),0)+
			max(floor(uv.y),0)
		),0,1);
}

void fragment() 
{
	vec2 shrunkUV = (UV*1.414)-0.2;
	float rotateAmount = rotationSpeed * TIME;
	
	vec2 clockwiseUV = rotateUV(shrunkUV, vec2(0.5), rotateAmount);
	vec4 clockwiseGlow = texture(TEXTURE, clockwiseUV) * maskUV(clockwiseUV);
	shrunkUV.x = 1.0-shrunkUV.x;
	vec2 anticlockwiseUV=rotateUV(vec2(1,0)+(shrunkUV*vec2(-1,1)), vec2(0.5), -rotateAmount*0.75);
	vec4 anticlockwiseGlow = texture(TEXTURE, anticlockwiseUV)*maskUV(anticlockwiseUV);
	
	vec4 result = mix(clockwiseGlow, anticlockwiseGlow, 0.5);
	result = clockwiseGlow*anticlockwiseGlow;
	COLOR = modulate;
	COLOR.a *= result.r*result.a;
}
