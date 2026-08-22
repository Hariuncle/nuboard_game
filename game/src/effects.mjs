const TAU = Math.PI * 2;

export function createImpactEffect({ x, y, hit }, random = Math.random) {
  const particleCount = hit ? 18 : 5;
  const particles = Array.from({ length: particleCount }, (_, index) => {
    const angle = (index / particleCount) * TAU + (random() - 0.5) * 0.55;
    const speed = (hit ? 0.16 : 0.06) + random() * (hit ? 0.22 : 0.08);
    return {
      x: 0,
      y: 0,
      vx: Math.cos(angle) * speed,
      vy: Math.sin(angle) * speed - (hit ? 0.025 : 0),
      rotation: random() * TAU,
      spin: (random() - 0.5) * 8,
      size: (hit ? 5 : 3) + random() * (hit ? 9 : 4),
      kind: hit && index % 3 !== 0 ? "petal" : "dust",
    };
  });

  return { x, y, hit, life: 1, age: 0, particles };
}

export function advanceImpactEffects(effects, deltaSeconds) {
  return effects
    .map((effect) => ({
      ...effect,
      age: effect.age + deltaSeconds,
      life: effect.life - deltaSeconds * (effect.hit ? 1.35 : 2.2),
      particles: effect.particles.map((particle) => ({
        ...particle,
        x: particle.x + particle.vx * deltaSeconds,
        y: particle.y + particle.vy * deltaSeconds,
        vy: particle.vy + deltaSeconds * 0.24,
        rotation: particle.rotation + particle.spin * deltaSeconds,
      })),
    }))
    .filter((effect) => effect.life > 0);
}
