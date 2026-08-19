# ClimateColumn

[![build](https://github.com/MarkAndersUK/ClimateColumn/actions/workflows/build.yml/badge.svg)](https://github.com/MarkAndersUK/ClimateColumn/actions/workflows/build.yml)
[![Licence: MIT](https://img.shields.io/badge/Licence-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

A one-dimensional radiative–convective climate model of a vertical column of atmosphere,
written in C# (.NET 8). The column is divided into segments; longwave emission from each
segment is the **Koenigsberger equation** and the surface radiates by **Stefan–Boltzmann**.
The model marches to equilibrium and reports the full flux profile.

```
dotnet run --project src/ClimateColumn.Cli -- --compare-convection --csv profile.csv
```

---

## The physics

### Emission: the Koenigsberger equation

Each segment emits, per unit volume and integrated over all directions,

$$\mathrm{d}q = 4\,\varepsilon'\,\sigma T^4\,\mathrm{d}V$$

where $\varepsilon'$ is the volumetric emission coefficient (m⁻¹) and $\sigma$ the
Stefan–Boltzmann constant. In a plane-parallel column, per unit horizontal area,
$\mathrm{d}V \to \mathrm{d}z$, so a segment of thickness $\mathrm{d}z$ emits
$4\varepsilon'\sigma T^4\,\mathrm{d}z$ in total.

### Why this fixes the diffusivity factor at D = 2

That emission is isotropic, so it splits evenly between the upward and downward
hemispheres: $2\varepsilon'\sigma T^4\,\mathrm{d}z$ each way. The hemispheric emissivity of
the slab is therefore $a = 2\varepsilon'\,\mathrm{d}z$, and Kirchhoff's law forces its
hemispheric *absorptivity* to the same value. So the extinction coefficient in flux space
is $D\varepsilon'$ with

$$D = 2 .$$

This is not an approximation chosen for convenience — it is the exact optically thin limit
of the angular integral, since the true hemispheric transmission $2E_3(\tau)$ expands as
$1 - 2\tau$ as $\tau \to 0$. The familiar $D = 1.66$ is a best fit across a broad range of
optical depth; **D = 2 is the value consistent with the Koenigsberger equation as written**,
and the model defaults to it. You can set any other value with `--diffusivity`, and the
summary will tell you when the correspondence no longer holds.

### The flux solver

With that closure, the Schwarzschild equations for the hemispheric fluxes are

$$\frac{\mathrm{d}F^{\uparrow}}{\mathrm{d}z} = -D\varepsilon'\left(F^{\uparrow} - \sigma T^4\right),
\qquad
\frac{\mathrm{d}F^{\downarrow}}{\mathrm{d}z} = +D\varepsilon'\left(F^{\downarrow} - \sigma T^4\right).$$

Integrating across a segment of constant temperature gives the recurrence the solver
actually uses, with $\tau_i = D\varepsilon'_i\,\mathrm{d}z_i$:

$$F^{\uparrow}_{i+1} = F^{\uparrow}_{i}e^{-\tau_i} + \left(1 - e^{-\tau_i}\right)\sigma T_i^4$$

and its mirror downward, starting from $F^{\downarrow} = 0$ at the top of the column. This
is stable at arbitrary optical thickness, conserves energy exactly, and reduces to the
differential Koenigsberger form as $\mathrm{d}z \to 0$.

The surface is a Stefan–Boltzmann emitter, $F = \varepsilon_s\sigma T_s^4$, which also
reflects the remaining $(1-\varepsilon_s)$ of the incident back radiation.

### Convection

Three non-radiative terms, all switchable via `--convection none|surface|full`:

- **Surface sensible heat**, $Q_c = h_c\,(T_s - T_{\text{air}})$, with the wind-speed
  relation $h_c = 5.8 + 4.1\,v$ from Koenigsberger et al., *Manual of Tropical Housing and
  Building*. The air temperature is extrapolated to $z=0$ from the two lowest segments.
- **Surface latent heat** — evaporation — described below. **Off by default.**
- **Critical-lapse-rate adjustment** on the atmospheric segments: wherever the lapse rate
  exceeds 6.5 K km⁻¹, the affected block is mixed onto exactly that lapse rate while
  conserving $\sum C_i T_i$.

The surface is deliberately **excluded** from the adjustment. It exchanges heat with the
air only through radiation and the turbulent fluxes; letting the adjustment mix the surface
reservoir as well would double count that transfer and leave the surface energy budget open.

#### Latent heat

On Earth evaporation carries roughly 80 W m⁻² off the surface against about 20 for sensible
heat, which makes it the largest non-radiative term in the surface energy budget. It enters
through the bulk aerodynamic form, written through the coefficient the sensible flux already
uses:

$$
LE = \beta\,\frac{h_c}{c_p}\,L\,\bigl[\,q_{\text{sat}}(T_s,p_s) - \text{RH}\;q_{\text{sat}}(T_{\text{air}},p_s)\,\bigr]
$$

Heat and vapour are carried by the same eddies, so $h_c = \rho c_p C_H v$ gives
$h_c/c_p = \rho C_H v$, the mass transfer coefficient in kg m⁻² s⁻¹, on the assumption
$C_E = C_H$ — close to true over water, and what lets one wind-speed relation drive both
fluxes. Saturation humidity comes from Clausius–Clapeyron integrated at constant $L$,

$$
e_{\text{sat}}(T) = e_0 \exp\!\left[\frac{L}{R_v}\!\left(\frac{1}{T_0} - \frac{1}{T}\right)\right],
\qquad
q_{\text{sat}} = \frac{\varepsilon\,e_{\text{sat}}}{p - (1-\varepsilon)\,e_{\text{sat}}},
\qquad \varepsilon = \frac{R_d}{R_v} = 0.622
$$

the same curve that scales the water-vapour absorber, so evaporation and the greenhouse
feedback cannot drift apart. $\beta$ (`--moisture`) is surface moisture availability and
scales the **whole flux**: the surface itself is saturated, and $\beta$ is the fraction of
potential evaporation a surface that cannot supply water fast enough actually delivers.
Scaling the surface *humidity* instead would put a dry surface below the overlying air and
drive perpetual dew deposition rather than merely suppressing evaporation. RH
(`--humidity`, default 0.8) is the near-surface relative humidity — fixed, because the model
carries no prognostic moisture to evolve it.

The integrator needs $\partial LE/\partial T_s$ for its stability limit, and this is not
$q\,(L/R_v)/T^2$: the $(1-\varepsilon)e$ in the denominator moves too, contributing a factor
$p/(p-(1-\varepsilon)e)$, about 1.007 at 288 K. Near 288 K with open water the derivative is
31 W m⁻² K⁻¹ — larger than $h_c$ and larger than the Planck term $4\varepsilon\sigma T^3$ —
so leaving it out of the limit lets the surface oscillate instead of settling.

Latent heat is delivered to the lowest segment. Condensation really happens spread through
the convecting layer, but the placement is immaterial under `--convection full`: the
adjustment mixes that block to the critical lapse rate conserving enthalpy, so heat added
anywhere inside it gives the same adjusted profile. That equivalence is also why the flux is
zero without convection.

**Why it is off by default.** $h_c$ was calibrated with the sensible flux standing in for
both, and the model's documented equilibrium follows from that calibration. Switching
evaporation on therefore gives a *different* model rather than a more accurate version of
this one — the surface cools and $h_c$ would need refitting to put it back. `--moisture 0`
is exactly zero, not a small residue, so every result below stands unchanged.

What happens when it is on:

| `--moisture` | LE (W m⁻²) | H (W m⁻²) | Bowen | $T_s$ (K) | convecting top |
|---:|---:|---:|---:|---:|---:|
| 0 (default) | 0 | 35.85 | — | 286.797 | 3.44 km |
| 0.2 | 21.32 | 18.12 | 0.85 | 286.339 | 4.06 km |
| 0.35 | 32.80 | 8.64 | 0.26 | 286.078 | 4.06 km |
| 0.7 | 51.17 | −6.53 | −0.13 | 285.660 | 4.06 km |
| 1.0 | 61.51 | −15.07 | −0.24 | 285.423 | 4.06 km |

Near $\beta = 0.35$ the Bowen ratio lands on Earth's global mean of about 0.25 — the model
can reproduce the **partition** between the two fluxes. It cannot reproduce the
**magnitude**: the total turbulent flux there is 41 W m⁻² against Earth's ~100, and no
choice of $\beta$ closes that gap, because the ceiling is $h_c$ — a building-physics film
coefficient, not a global-mean bulk transfer coefficient. Past $\beta \approx 0.5$ the
sensible flux goes *negative*: evaporative cooling drops the surface below the air it is
coupled to, which is what an over-tight surface–air coupling does when a second loss term
is added to it.

### Clouds

Clouds do two opposite things, and the model does both. **Off by default** —
`--cloud-fraction 0` — so every number quoted elsewhere in this README is unaffected.

**Shortwave: they reflect.** The albedo splits into a cloud-free part and a cloudy part, mixed
by cloud fraction:

$$A = (1-f)\,A_{\text{clear}} + f\,A_{\text{cloud}}$$

This is the step that stops the clouds being counted twice. Earth's 0.30 planetary albedo
*already contains* the clouds; adding a reflective deck on top of it would reflect the same
sunlight again. With $A_{\text{clear}} = 0.155$, $A_{\text{cloud}} = 0.361$ and $f = 0.67$ the
mix returns 0.293 — Earth's all-sky albedo — so switching clouds on does not change how much
sunlight the planet takes in.

**Longwave: they trap.** The deck is specified by the emissivity it should have as a whole,
inverted to a hemispheric optical thickness $\tau = -\ln(1-\varepsilon)$ and spread over the
segments by their **overlap** with the cloud rather than by their full thickness — so a
1.0–4.5 km deck keeps the same emissivity however the column is divided up.

Cloud opacity is **grey**, and that is not a shortcut but the physics: droplets absorb across a
band where gas absorbs in lines. So it is added outside the k-distribution rather than inside
it. Adding it to a band's mean optical depth would have made the cloud thin wherever the gas is
transparent — which is the exact opposite of what a cloud does, since its longwave work happens
mostly in the window, where the gas lets the surface radiate straight to space.

**Two skies, one atmosphere.** The longwave is solved twice, with and without the deck, and the
fluxes mixed by cloud fraction. This is the independent column approximation. The mixing is
exact — fluxes add, so a sky that is $f$ cloudy really does emit the weighted mean — and what is
approximate is the premise: that the cloudy and clear parts of the sky do not exchange radiation
sideways. Both solves see the same temperatures, because there is one atmosphere and one
surface under both skies.

#### What the model gets, and one thing it found

`ModelOptions.WithTypicalCloud()` is calibrated against the CERES satellite record:

| Cloud radiative effect | Model | CERES |
|---|---:|---:|
| Shortwave (reflected away) | −46.96 | −47.1 |
| Longwave (trapped) | +26.55 | +26.2 |
| **Net** | **−20.41** | **−20.9** |

W m⁻², measured as CERES measures it — this planet against the same planet with the cloud
removed and nothing else touched.

Only the longwave figure is a result. The shortwave one is arithmetic on the two albedos and
the cloud fraction, and those were chosen to reproduce Earth's albedos.

**The calibration is separate from the default's, and finding out why was the interesting
part.** Switching clouds on over the shipped configuration warms the surface by about 13 K.
Nothing is broken: that configuration's absorbers were scaled to reach an Earth-like
temperature on a planet with an 0.30 albedo and *no cloud* — a planet carrying the clouds'
reflection while having none of their greenhouse. The gas had been quietly standing in for the
cloud greenhouse all along, so adding a real cloud supplies it twice.

So the cloudy configuration halves the gas optical depth, from 1.8 to 1.1329, and hands the
difference to the deck. Two constraints, two knobs: the deck's top height sets how much
longwave it traps, the gas loading sets the surface temperature. Solving both together lands on
a 1.0–4.5 km deck over 67 % of the sky, at 286.796 K — the same surface as the cloud-free
default, reached a different way.

There is a second result in that. Running the four corners — each effect on and off — the two
nearly cancel in **temperature** while emphatically not cancelling in **flux**:

| At the calibrated loading | Surface | Change |
|---|---:|---:|
| Clear albedo, no cloud longwave | 287.79 K | — |
| All-sky albedo, no cloud longwave | 275.33 K | −12.45 K |
| Clear albedo, with cloud longwave | 299.53 K | +11.75 K |
| Both — the calibrated configuration | 286.80 K | **−0.99 K** |

A net cloud effect of −20.41 W m⁻² produces about **one** kelvin of cooling, not the twelve a
single sensitivity applied to that flux would suggest. The reason is that the two components
have different efficacies here: 0.27 K per W m⁻² for the shortwave against 0.44 for the
longwave. Read that as a property of *this* model — a fixed 6.5 K km⁻¹ lapse rate and a
prescribed deck that cannot respond — rather than as a claim about Earth. It does illustrate
why cloud feedback is the largest remaining uncertainty in real climate sensitivity: a net
number this small is a difference between two much larger ones.

### Spherical geometry

By default the column is plane-parallel: a stack of infinite slabs of constant cross-section,
which is where every flux in W m⁻² comes from. `--spherical` treats it instead as a set of
shells on a planet of radius $r_0$, where a shell at radius $r$ has $(r/r_0)^2$ times the area
of the surface beneath it. Two consequences follow, and one non-consequence:

- It holds that much more mass, so it has that much more heat capacity and emits that much
  more power.
- Radiation leaving it spreads over a growing area. In the flux divergence this is the
  $-(2/r)F$ term:

$$
\frac{\mathrm{d}F^{+}}{\mathrm{d}r} = -D\varepsilon'\bigl(F^{+} - \sigma T^4\bigr) - \frac{2}{r}F^{+}
$$

- **Optical depths do not change.** They are path integrals along a radial ray, and a wider
  shell is no more opaque from below.

The implementation rests on one identity. Writing the equation above in terms of

$$
G^{\pm}(r) = \left(\frac{r}{r_0}\right)^{2} F^{\pm}(r)
$$

— the power crossing radius $r$ divided by the surface area beneath it — eliminates the
geometric term entirely:

$$
\frac{\mathrm{d}G^{+}}{\mathrm{d}r} = -D\varepsilon'\left(G^{+} - \left(\frac{r}{r_0}\right)^{2}\sigma T^4\right)
$$

which is exactly the plane-parallel equation with its **source scaled by $(r/r_0)^2$**. So the
exponential recurrence stays exact, the boundary conditions are untouched ($G = F$ at the
surface, and nothing enters the top in either variable), and every energy budget still closes
in W per m² of planet surface. Sphericity therefore enters the solver in one line — the
emitted power — and nowhere else.

Each segment's factor is the **exact shell volume**, $(r_t^3 - r_b^3)/(3r_0^2\,\mathrm{d}z)$,
rather than $(r_\text{mid}/r_0)^2$, so a segment holds exactly the mass a shell holds and emits
exactly the power one emits. The residual approximation is that the factor is held constant
across a segment, second order in d$z$ and about $3\times10^{-4}$ relative for a 1 km segment.

Because the reported fluxes are power per unit *surface* area, the outgoing longwave still
balances the absorbed solar at 238.175 W m⁻²; the radiant flux actually crossing the top is
that spread over 1.6 % more area, 234.480 W m⁻².

**What it costs.** The surface cools by **0.016 K** — 286.797 → 286.781 K. Far less than the
1.6 % area change suggests, because the extra mass sits in the thin cold stratosphere while
the greenhouse effect is made near the ground where the factor is still 1. The CO₂ doubling
response moves by 0.025 K out of 27.37 K, 0.09 %, so it is very nearly a constant offset that
cancels in differences. Off by default, on the same grounds as the latent flux: it shifts the
documented equilibrium, and everything below was produced without it.

That smallness is a property of Earth, not of the code — at $r_0 = 200$ km the shift is large,
and the suite checks that it scales as $H/r$ and vanishes as $r \to \infty$. The central test
integrates the *original* spherical equation with fourth-order Runge–Kutta and compares, so the
$G$ reformulation is verified rather than assumed; inverting the factor's sign fails five tests,
including all three integration cases.

### Gravity falling with height

`--variable-gravity` replaces the sea-level constant with the inverse-square law,
$g(z) = g_0\bigl(r_0/(r_0+z)\bigr)^2$ — 9.8066 m s⁻² at the surface, 9.6545 at 50 km, a fall of
1.55 %.

The subtlety is not $g$ but where it belongs. **The U.S. Standard Atmosphere is defined on
*geopotential* altitude** with gravity fixed at the defined constant $g_0$. That is not a
simplification in the standard — it is how the standard absorbs the variation of gravity, by
measuring height in work done against gravity rather than in metres:

$$
\Phi(z) = \int_0^z g_0\left(\frac{r_0}{r_0+z'}\right)^{2}\mathrm{d}z' = \frac{g_0 r_0 z}{r_0+z},
\qquad
H \equiv \frac{\Phi}{g_0} = \frac{r_0 z}{r_0 + z}
$$

so 50 geometric km is **49.61 geopotential km**. Switching gravity on therefore changes two
things together:

- The profile is read at $H$ rather than at $z$, so pressure falls off more slowly with
  geometric height.
- A segment's mass is $\mathrm{d}p/g(z)$ rather than $\mathrm{d}p/g_0$ — weaker gravity aloft
  means more mass is needed to hold up the same pressure drop.

Those two have to be *mutually* consistent, and that consistency is the real content of the
change. Because $\mathrm{d}H/\mathrm{d}z = g/g_0$, reading the tables at $H$ and dividing by the
local $g$ reproduces $\mathrm{d}p/\mathrm{d}z = -\rho g$ exactly. The test that checks it
compares each segment's density from the mass grid against the ideal gas law applied to the
profile it was built from: pairing the geopotential grid with a constant $g$ leaves a ~1.5 %
residue, and does fail.

Optical depths are again untouched — they are normalised to a prescribed column total — though
the mass *distribution* shifts very slightly upward. The column gains about 0.25 % mass overall,
weighted by where the air actually is rather than the full 1.55 % available at the top.

**The finding worth keeping.** Gravity and sphericity move the surface in **opposite**
directions and very nearly cancel:

| configuration | $T_s$ (K) | shift |
|---|---:|---:|
| plane-parallel, constant $g$ (default) | 286.797 | — |
| `--variable-gravity` | 286.809 | **+0.012 K** |
| `--spherical` | 286.781 | **−0.016 K** |
| `--spherical --variable-gravity` | 286.794 | **−0.003 K** |

Variable gravity puts more mass in the column and warms it; sphericity puts more *emitting* mass
aloft and cools it. Either alone overstates the combined correction by roughly fivefold, which is
why they are separate flags rather than one folded into the other — `--spherical
--variable-gravity` is the physically complete pairing, and the individual flags exist to show
which term does what.

One thing the relaxation does **not** reach: the dry adiabat $g/c_p$ falls from 9.761 to
9.609 K km⁻¹, but the convective adjustment runs on the prescribed critical lapse rate of
6.5 K km⁻¹, not on $g/c_p$, so convection does not see it.

### The top-of-atmosphere solar cross-section

The planet intercepts sunlight on a disc. `--toa-interception` makes that disc the top of the
atmosphere, radius $r_0 + H$, rather than the solid planet, radius $r_0$ — 1.58 % more area, so
5.362 W m⁻² more intercepted power per unit surface area.

**The obvious implementation is wrong, and wrong twice.** Multiplying `AbsorbedSolarFlux` by
$(r_\text{top}/r_0)^2$ adds 3.753 W m⁻², puts it through the surface albedo, and delivers 78 % of
it to the ground. But the extra light is **limb light**: rays with impact parameter between $r_0$
and $r_\text{top}$ graze through the atmosphere and out the other side. They never touch the
surface, so the surface albedo has nothing to act through and the ground gets none of it — and
only part of the annulus is absorbed at all, because a ray grazing at 40 km passes through almost
no air.

So what is added is a slant-path integral over impact parameter. For each $b$ the ray descends to
a tangent point at radius $b$ and climbs back out, crossing every shell above $b$ twice, with
path length through shell $i$ on one leg

$$
\Delta s_i(b) = \sqrt{r_{t,i}^2 - b^2} - \sqrt{\max(r_{b,i},\,b)^2 - b^2}
$$

The beam is walked in order so absorption is attenuated by everything already traversed, and the
annulus is integrated with the area weight $2\pi b\,\mathrm{d}b$ over the planet's surface area
$4\pi r_0^2$. Two choices worth naming rather than burying:

- **The extinction coefficient is inferred, not given.** The model's shortwave is a prescribed
  deposition profile, not a radiative transfer calculation, so there is no coefficient to reuse.
  One is constructed by asking what vertical optical depth reproduces the prescribed absorption,
  $\tau = -\ln(1-f)$, distributed with the same mass-and-Chapman shape the deposition uses. That
  makes the limb path consistent with the vertical one *by construction* — but reading $f$ as a
  disc-averaged slant absorption instead would give a larger $\tau$ and more limb capture.
- **No albedo is applied**, since a limb ray never reaches the surface and the model has no
  scattering. This makes the limb term an upper bound relative to treating it as equally
  reflective.

**What it does.** Of the 5.362 W m⁻² intercepted, **2.563 W m⁻² is absorbed (47.8 %)** — a third
less than the naive rescaling — and it lands **high**, centred at 19.2 km with its peak at
14.7 km, against the vertical beam's 7.3 km centroid. The surface warms **+0.309 K**, an order of
magnitude more than sphericity or gravity, because this one changes *how much energy the planet
absorbs* rather than redistributing what it already had.

Because the total absorbed is no longer a property of the options alone, `Column.TotalShortwaveAbsorbed`
is what the outgoing longwave balances at equilibrium, and the suite checks that everything
deposited in the segments and the surface sums to exactly that.

### The three geometric corrections together

| configuration | $T_s$ (K) | shift |
|---|---:|---:|
| plane-parallel, constant $g$, planet disc (default) | 286.797 | — |
| `--variable-gravity` | 286.809 | +0.012 K |
| `--spherical` | 286.781 | −0.016 K |
| `--toa-interception` | 287.106 | **+0.309 K** |
| all three | ~287.10 | **+0.31 K** |

The two that redistribute energy nearly cancel; the one that changes the energy input dominates by
an order of magnitude. All three are off by default.

### Beyond the grey column

Four optional pieces of physics, all off by default, so the baseline above is unchanged
unless you ask for them.

**A spectral window** (`--window-from-um`, `--window-to-um`). A wavelength interval is made
transparent and the rest of the spectrum stays grey. The window share of the surface
emission escapes to space unattenuated; because it is the same at every interface it drops
out of the flux divergence, so it warms nothing on the way up — which is the point.

The window is specified as an **interval, not a fraction**, because a fraction is not a
property of the atmosphere on its own — it depends on the temperature of whatever is
emitting. For 8–13 µm:

| | share of emission |
|---|---|
| 287 K surface | 31.1 % |
| 255 K emission temperature | 27.0 % |
| 217 K tropopause | 20.0 % |

A single number is therefore wrong at one end by more than a factor of 1.5, and the cold end
is exactly where the outgoing longwave is set. Each emitter's share instead follows from its
own Planck function via the fractional-blackbody integral, which depends only on the product
$\lambda T$ — so one series evaluation covers every wavelength and temperature. Every source
term carries its own $(1-f(T))$, and each emitter still divides exactly its own $\sigma T^4$
between band and window, so energy closure is untouched.

The one-slab benchmark survives, with $f$ now evaluated at the surface's own temperature:

$$T_s = \left(\frac{2}{1+f(T_s)}\right)^{1/4} T_e.$$

That is implicit in $T_s$ and has to be solved for, which makes it a **stronger** test than
the flat-window version, where $f$ was simply whatever the caller passed in. It still
recovers the classic $2^{1/4}T_e$ for no window.

**The window is not actually transparent** (`--continuum-tau`, `--continuum-foreign`). What
closes it over the humid tropics is the water-vapour continuum — absorption between the lines,
conventionally split into a self term going as vapour pressure squared and a foreign term
going as vapour pressure times air pressure,

$$k \sim e\left(C_s e + C_f p\right).$$

Both scalings are reproduced, with `--continuum-foreign` setting their balance at the
reference state. Strength is one tunable number rather than fitted MT_CKD coefficients — the
rest of the model is grey, so spectral coefficients here would be false precision.

The quadratic self term is the behaviour that matters. Warm the column, Clausius–Clapeyron
raises the vapour, and the continuum grows as roughly its square, so **the window shuts as
the climate warms** — something a fixed transparent window can never do. Doubling the vapour
multiplies the continuum by $f\cdot 2 + (1-f)\cdot 4$, which the tests assert exactly for
$f = 0, 0.25, 0.5, 1$.

Given a continuum the window becomes a genuine second band that absorbs and emits, so the
solver now runs both bands through the same recurrence and sums them. That reduces *exactly*
to the transparent case at zero continuum — a band with $\tau = 0$ has unit transmittance, so
it neither absorbs nor emits — which is asserted bit-identically rather than to a tolerance.

One consequence is worth knowing: an opaque continuum traps only about half the window flux,
not nearly all of it. Because the continuum follows the vapour and the self term squares it,
it is heavily bottom-heavy, so the window goes opaque close to the ground and keeps emitting
from air only a little cooler than the surface. More continuum raises that emission level
slowly, so the trapped fraction climbs monotonically but never approaches one.

**Line structure within a band** (`--k-distribution`, `--k-width`, `--k-points`). Absorption
inside a real band spans orders of magnitude — strong line cores, weak wings — and a single
coefficient systematically overstates how opaque the band is, because transmission is
dominated by the wings rather than the mean. A correlated-k quadrature fixes that.

Transmission depends only on the *distribution* of $k$ across the band, not on where in the
band each value sits, so reordering $k$ by magnitude gives a monotonic $k(g)$ over the
cumulative fraction $g$ and

$$T(u) = \int_0^1 e^{-k(g)u}\,\mathrm{d}g \;\approx\; \sum_j w_j e^{-k_j u}.$$

Each $(w_j, k_j)$ is a pseudo-monochromatic sub-band run through the ordinary recurrence, and
the results summed. Two properties are enforced by construction, and both matter: the weights
sum to 1, and the weighted mean of $k_j$ is exactly the band mean. The second is what stops a
k-distribution from quietly changing how much absorber the column holds — and it means the
**optically thin limit is untouched**, since $\langle 1 - e^{-ku}\rangle \to \langle k\rangle u$,
so the Koenigsberger correspondence and the $D = 2$ closure survive unchanged. Both are
asserted.

The quadrature rule matters more than it looks. Sampling the inverse CDF at equal-probability
midpoints is the obvious approach and converges appallingly — about $N^{-1/2}$, because $k(g)$
is unbounded at both ends, leaving a 7 % transmission error at 16 points for a width-2
lognormal. Integrating instead in the variable where the density is standard fixes it:

| 16 g-points, width 2 | transmission error |
|---|---|
| equal-probability midpoint | 6.6 × 10⁻² |
| Gauss–Legendre in $g$ | 2.1 × 10⁻² |
| **Gauss–Hermite in $\log k$** | **2.9 × 10⁻⁴** |

So the lognormal shape uses Gauss–Hermite. Width 0 collapses to exactly one sub-band, so a
grey band is the one-point case and every existing configuration is bit-identical. An
`exponential` shape is also available: that is the Goody random band model, whose transmission
has the closed form $1/(1+ku)$, and the quadrature is verified against it.

The k-distribution applies to the absorbing band only. The continuum stays grey deliberately —
smoothness between the lines is what makes it a continuum.

Admitting line structure lets more longwave out at fixed temperature and cools the equilibrium
surface: the same gas does less greenhouse work once its structure is honest.

### Solving band by band

Everything above describes one absorber standing in for the whole longwave spectrum, with the
window as a second band. That cannot represent two gases at once — CO₂'s 15 µm band and water
vapour's rotational band differ in strength, in where they sit vertically, and in line structure
by two orders of magnitude. `ModelOptions.Bands` makes each expressible:

```csharp
Bands = new[]
{
    new SpectralBand { Label = "H2O rotational", ShortWavelength = 20e-6, LongWavelength = 50e-6,
                       WaterVapourOpticalDepth = 4.0, Co2Fraction = 0.0, Structure = waterK },
    new SpectralBand { Label = "CO2 15 um", ShortWavelength = 13e-6, LongWavelength = 17e-6,
                       OpticalDepth = 3.0, Structure = carbonK },
    new SpectralBand { Label = "window", ShortWavelength = 8e-6, LongWavelength = 13e-6,
                       ContinuumOpticalDepth = 0.4 },
    new SpectralBand { Label = "remainder", OpticalDepth = 0.6, Co2Fraction = 0.0 }
}
```

Each band carries its own optical depth, its own mix of well-mixed gas, water vapour and
continuum — each keeping its own vertical profile — and its own k-distribution, which is where a
distribution measured from HITRAN belongs. Only bands with a non-zero `Co2Fraction` respond to
`--co2-ppm`, so doubling CO₂ moves the CO₂ band and leaves water vapour alone.

A band's share of an emitter's radiation is the fraction of that emitter's Planck function inside
its interval, so it follows temperature. **One** band may instead be the *remainder*, carrying
whatever the intervals leave — needed because "everything except the window" is not itself an
interval. If no band claims the remainder, the solver adds a transparent one: without it the
weights sum to less than one and the surface silently radiates less than its own $\sigma T^4$,
with the difference vanishing into the part of the spectrum nobody described. Overlapping
intervals are rejected for the mirror-image reason.

The single-absorber arrangement is now expressed as a band set too, so there is one code path,
and it reproduces bit-identically — every prior test passed unchanged across the change.

#### Deriving the bands from line data

Specifying bands by hand means three guesses each: where the band sits, how opaque it is relative
to its neighbours, and what its line structure looks like. Given a line list, all three follow
from the data.

```csharp
var bands = BandDerivation.Combine(remainderOpticalDepth: 0.4,
    BandDerivation.Derive(waterSpectrum,  4, opticalDepth: 4.0, AbsorberKind.WaterVapour, "H2O"),
    BandDerivation.Derive(carbonSpectrum, 4, opticalDepth: 2.0, AbsorberKind.WellMixed,   "CO2"));
```

Boundaries come from the Planck function — each band carries an equal share of it at a reference
temperature, so bands are narrow where the emission is concentrated (`UniformWavenumber` is
available for comparison). Relative opacity is the mean absorption measured inside each band.
Structure is the measured distribution of that absorption. The one free parameter is the
Planck-weighted mean optical depth, which is right: that is concentration, not spectroscopy.

Derived from HITRAN, it recovers band structure nobody told it about:

| CO₂ band | τ | | H₂O band | τ |
|---|---|---|---|---|
| 15.9–16.7 µm | 0.90 | | 22.2–24.7 µm | 1.12 |
| 15.2–15.9 µm | 9.77 | | 24.7–27.9 µm | 2.72 |
| **14.6–15.2 µm** | **44.11** | | 27.9–32.7 µm | 8.16 |
| 13.9–14.6 µm | 6.08 | | 32.7–41.1 µm | 26.78 |
| 13.3–13.9 µm | 0.79 | | 41.1–66.7 µm | 37.94 |

The most opaque CO₂ band straddles **15.0 µm** — the ν₂ centre — and is 56× more opaque than the
wings. That contrast *is* the band saturation a grey model cannot express, and it emerged from the
line strengths. Water vapour strengthens monotonically into the far infrared, as a rotational
progression should. Both boundary strategies find the same peak to within 25 %, so the pattern
comes from the spectrum rather than from where the edges fall.

A fully derived two-gas column converges with the top of atmosphere balanced to 10⁻⁶ W m⁻², and
doubling CO₂ warms it while leaving the water bands untouched.

#### Several molecules on a shared grid

Deriving each gas separately produces sets that *overlap* — N₂O's band sits inside methane's, water
vapour is everywhere — and overlapping bands are rejected, rightly. `DeriveShared` puts every
molecule on one wavenumber grid, partitions the range once, and records how much each gas
contributes to each band:

```
pwsh scripts/fetch-hitran.ps1 -Molecule all      # H2O x2, CO2, O3, CH4, N2O
```

Twelve bands over 100–2000 cm⁻¹ from ~100,000 lines, in under a second:

| band | dry τ | CO₂ frac | vapour | ozone | |
|---|---|---|---|---|---|
| 37.8–100 µm | — | — | **55.5** | — | H₂O rotational |
| 20.2–23.4 µm | — | — | 0.74 | — | |
| 14.1–15.8 µm | **48.3** | 1.00 | — | — | CO₂ ν₂ centre |
| **11.2–12.6 µm** | — | — | — | — | **the atmospheric window** |
| 9.8–11.2 µm | — | — | — | **2.05** | O₃ 9.6 µm |
| 5.0–8.2 µm | 0.73 | 0.00 | 4.91 | — | H₂O bending + CH₄/N₂O |

These bands carry **98.9 %** of a 260 K Planck function, so the remainder — the one free number
standing in for everything unmodelled — is down to 1.1 %.

Nothing told the derivation where the atmospheric window was; it is simply where none of these gases
has lines. Nothing told it ozone sits inside that window either. Each band's `Co2Fraction` is CO₂'s
share of its well-mixed opacity, so only the bands CO₂ dominates respond to concentration — asserted
band by band. Ozone gets its own Chapman profile, since it peaks in the stratosphere and would be in
entirely the wrong place on either of the other two; the tests check its absorption peaks between
15 and 40 km.

The real approximation is **gas overlap**: a band's k-distribution is measured from the *total*
absorption of every gas in it at reference amounts, so moving one gas far from its reference degrades
the distribution while leaving its optical depth correct. That is the classic difficulty in
correlated-k, and it is bounded here rather than solved — re-derive if a concentration moves by more
than a factor of a few.

One clean identity is genuinely lost. Under a flat fraction the instantaneous forcing scaled
as exactly $(1-f)$, because $f$ factored out of every source term. It no longer does. It is
tempting to assume the suppression must then be bracketed by $(1-f)$ at the profile's
extremes, but that is false: the forcing is linear in the per-segment band weights with
**mixed signs** — attenuating the surface term raises it while atmospheric emission
compensates and lowers it — so there is no convex combination and no bracket. The measured
suppression duly sits below even the warmest single-temperature bound. What the tests assert
instead is that the suppression is measurably different from what the surface's share alone
would predict; if it were not, the temperature dependence would be cosmetic.

**Pressure broadening** (`--pressure-broadening n`). The dry absorber is distributed as
$\varepsilon' \sim \rho\,(p/p_0)^n$ rather than $\varepsilon' \sim \rho$, renormalised to
the same column optical depth, so $n$ moves the absorber up and down without changing how
much of it there is. $n=1$ is the collision-broadened case and puts about 50% more optical
depth below 5 km.

**An ozone-like layer** (`--ozone-fraction x`). A share of the atmospheric solar absorption
is deposited on a Chapman profile $\exp(1 - x - e^{-x})$, $x = (z-z_0)/H$ — the shape of
absorption of an exponentially attenuated beam in an exponential absorber. At 0.3 it
produces a genuine stratospheric inversion, warming 25 km by about 25 K.

**Water vapour feedback** (`--wv-tau`). A second absorber, distributed as $e^{-z/H}$ with
$H \approx 2$ km rather than well mixed, whose column loading follows Clausius–Clapeyron on
the near-surface air temperature,

$$\tau_{v}(T) = \tau_{v,\text{ref}}\exp\left[\frac{L}{R_v}\left(\frac{1}{T_\text{ref}} -
\frac{1}{T}\right)\right] \approx +6.5\ \%\ \mathrm{K}^{-1}\ \text{near } 288\ \mathrm{K}.$$

It is re-evaluated before every radiation solve, so it is a real feedback rather than a
fixed profile: forcing experiments perturb the dry absorber and the vapour responds. It
raises the model's climate sensitivity from 0.59 to 0.72 K per W m⁻². It also brings the
feedback's genuine pathologies with it — a vapour-only column has a stable cold branch that
it will happily fall onto, and the strongly amplified configurations sit close to runaway.

---

## Verification

`ClimateColumn.Tests` is an MSTest project running on MSTest's own runner, so both
`dotnet test` and `dotnet run` work against it. 311 cases, plus 12 in `ClimateColumn.Charts.Tests`. The ones that matter are checks
against results derived independently of the code:

| Benchmark | Expected | Model |
|---|---|---|
| Transparent atmosphere | $T_s = T_e$ | exact to 10⁻³ K |
| One opaque slab | $T_s = 2^{1/4}T_e$ | ✓ |
| $N$ opaque slabs | $T_s = (N{+}1)^{1/4}T_e$, layer $k$ from top at $k^{1/4}T_e$ | ✓ for $N = 1,2,4$ |
| Opaque slab under a window | $T_s = \left(2/(1{+}f(T_s))\right)^{1/4}T_e$, solved implicitly | ✓ for no window, 8–13 µm, 8–20 µm |
| Fractional Planck function | 8–13 µm holds 31.05 % at 287 K, 19.98 % at 217 K | within 0.002 |
| Zero continuum | window band must stay exactly transparent | bit-identical |
| Continuum vapour scaling | doubling vapour multiplies it by $f\cdot2+(1-f)\cdot4$ | exact to 10⁻⁶ |
| Goody band model | k-quadrature must reproduce $T = 1/(1+ku)$ | within 0.01 |
| k-distribution mean | weights sum to 1, weighted mean $k$ is the band mean | exact to 10⁻¹² |
| Thin limit under structure | absorption → band-mean $\tau$ whatever the spread | relative 10⁻⁶ |
| Grey radiative equilibrium | $\sigma T_s^4 = F_0\left(1 + \tau/2\right)$ | within 0.05 K for $\tau = 0.5, 1.8, 4$ |
| Thin-layer limit | $2\left(1-e^{-\tau}\right)\sigma T^4 \to 4\varepsilon'\sigma T^4\mathrm{d}z$ only at $D = 2$ | ratio 1.000 at $D{=}2$, 0.830 at $D{=}1.66$ |
| Isothermal subdivision | slicing one isothermal slab must not change any flux | bit-identical for $n = 1 \ldots 256$ |
| Window suppression | must differ from the surface-share prediction, so $f(T)$ is not cosmetic | ✓ |
| Clausius–Clapeyron | 5 K warming multiplies the vapour loading by $e^{L/R_v(1/T - 1/T')}$ | exact to 10⁻⁶ |
| US Standard Atmosphere | $p(11\,\text{km}) = 22632$ Pa, $\rho_0 = 1.225$ kg m⁻³ | ✓ |

Plus: exact energy closure segment by segment and for the column (with and without a
window), enthalpy conservation and termination of the convective adjustment, grid
convergence (20 → 320 segments span less than 0.1 K), renormalisation of the absorber under
pressure broadening, conservation of solar flux under the ozone redistribution, and
rejection of every configuration that has no equilibrium.

### Checked against a resolved spectrum

Everything above is a *consistency* check — closed forms the solver should satisfy, budgets that
should balance — and consistency cannot tell you whether a band approximation is any good.
`LineByLineBand` resolves an explicit line list on a 60,000-point wavenumber grid with no band
approximation anywhere, and `LineByLineTests` compares against it.

The line list is **synthetic** — 90 Lorentz lines with exponentially distributed strengths, from
a documented rule with a fixed seed, not from HITRAN. So this validates the *method* against
exact spectral integration; it does not validate the model against Earth's spectrum, which would
need real line data.

Transmission through a homogeneous path, at band-mean optical depth τ:

| τ | line-by-line | grey | grey error | 16 g-points | 32 g-points |
|---|---|---|---|---|---|
| 0.3 | 0.835 | 0.741 | 0.094 | 0.0037 | 0.0010 |
| 1.0 | 0.688 | 0.368 | **0.320** | 0.0016 | 0.0004 |
| 3.0 | 0.511 | 0.050 | **0.461** | 0.0012 | 0.0003 |
| 10 | 0.287 | 0.00005 | 0.287 | 0.0008 | 0.0002 |

A grey band is not slightly wrong, it is wrong by half the dynamic range — at τ = 3 it transmits
5 % where the resolved spectrum transmits 51 %. Sixteen g-points get within 0.002.

The more interesting result is that the reference **separates two error sources that were
otherwise indistinguishable**. Through three layers spanning a threefold pressure range:

| | transmission | error |
|---|---|---|
| line-by-line | 0.717633 | — |
| correlated-k, 8 g-points | 0.709100 | 8.5 × 10⁻³ |
| correlated-k, 64 g-points | 0.716690 | 9.4 × 10⁻⁴ |
| correlated-k, 256 g-points | 0.716792 | 8.4 × 10⁻⁴ |
| grey, same absorber | 0.367879 | 3.5 × 10⁻¹ |

Quadrature error falls with g-points; the correlation error does not. Past about 64 points the
remainder is the correlated-k assumption itself, flooring near **8 × 10⁻⁴** — four hundred times
smaller than ignoring the structure, and a useful thing to know because it says when adding
g-points has stopped buying anything. That the same calculation at a single pressure throughout
agrees to 6 × 10⁻⁶ confirms the floor is the assumption and not a bug.

### Checked against real CO₂ and water vapour

The reference above validates the *method*. Whether the approximation resembles a real gas is a
different question, and it needs real line data:

```
pwsh scripts/fetch-hitran.ps1
pwsh scripts/fetch-hitran.ps1 -Molecule h2o-rotational
```

CO₂'s 15 µm band (28,619 lines) and water vapour's pure rotational band (4,219 lines) — no API
key required. The data is **not committed**: it is third-party data with its own citation
requirement, and leaving it out keeps the suite runnable with no network at all. The tests skip
rather than fail when it is absent — 198 pass + 19 skip with nothing fetched, 217 with both.

| τ | | line-by-line | grey | best lognormal | measured, 32 |
|---|---|---|---|---|---|
| 1.0 | CO₂ | 0.666 | 0.368 | 0.639 | 0.666 |
| 3.0 | CO₂ | 0.434 | 0.050 | 0.420 | 0.433 |
| 1.0 | H₂O | 0.808 | 0.368 | — | 0.807 |
| 3.0 | H₂O | 0.682 | 0.050 | — | 0.681 |
| 10 | H₂O | **0.504** | **0.00005** | — | 0.503 |

That last row is the clearest statement of what the grey assumption costs: at τ = 10 a grey band
transmits five thousandths of a percent where the real water-vapour band transmits **half**. And
it is worst for the gas that does most of the absorbing. The measured k-distribution tracks
line-by-line to ~0.001 for both molecules.

Water vapour is far more non-grey than CO₂ — its absorption spans a factor of 7.6 × 10⁴ against
CO₂'s 8.7 × 10², because CO₂'s 15 µm band is a regular vibration–rotation progression while H₂O
is an asymmetric rotor with lines scattered irregularly.

**The parametric families do not fit**, which is a negative result about this model's own knob:

- CO₂'s best-fitting lognormal width **drifts with optical depth** — 1.70 thin, 1.25 thick —
  because a real band's k-distribution is not lognormal.
- The two molecules want **different widths**: 2.40 for H₂O against 1.50–1.70 for CO₂. No single
  `--k-width` can serve an atmosphere containing both.
- H₂O is, interestingly, the better-behaved of the two: many irregular lines land closer to
  lognormal than one regular progression does, so its width barely drifts (2.30 → 2.35).

Hence `ModelOptions.MeasuredKDistribution` — given line data, use the band's real distribution
rather than a fitted shape.

Two simplifications are stated rather than buried: intensities are used at their 296 K values,
since scaling them properly needs total internal partition sums, and only air broadening is
applied. Neither matters for comparing band approximations, because the reference and the
approximation see identical line data.

The suite is organised by what it constrains — `AnalyticBenchmarkTests` and
`SolverConsistencyTests` hold the assertions above, `ExtendedPhysicsTests` and `Co2Tests`
the optional physics, `FullModelTests` the end-to-end budgets. Equilibrium runs are the
expensive part and several tests need the same configurations, so `TestSupport.Equilibrium`
memoises them. Runtime splits sharply on whether HITRAN data is present: without it the suite
takes a few seconds, because every spectral test skips. With it, the converged CO₂ sweep alone is
about 150 seconds and dominates everything else.

### Continuous integration

Two jobs, because the repository makes two promises worth checking separately.

**`build and test`** runs on `windows-latest`, because `ClimateColumn.Charts` targets
`net8.0-windows`. It restores, builds, runs both suites, then re-checks the documented
equilibrium through the CLI — 286.797 K, 238.175 W m⁻², convecting to 3.44 km. Those three
numbers are quoted throughout this README and nothing inside the suite pins all of them together
from the outside.

**`offline build, no package sources`** runs on `ubuntu-latest` and is the more interesting one.
It restores `ClimateColumn.Core` and `ClimateColumn.Cli` against the committed `nuget.config`
*as it stands*, with no source supplied — so it fails if either project ever acquires a package
dependency. That is the claim which makes the model usable air-gapped, and it is exactly the kind
of property that decays quietly. Running it on Linux also makes it a free check that the physics
is platform-independent, which nothing else tested; the equilibrium is compared there with a
0.01 tolerance rather than by string match, since `Math.Exp` and `Math.Pow` are not guaranteed
bit-identical across platforms.

`nuget.config` is left alone by both. It clears every package source deliberately, so the test job
supplies nuget.org on the restore *command line*, which overrides the cleared sources for that one
command without touching the offline-first configuration.

**The skip counts are the evidence, not noise.** No line lists exist on a runner, so a green run
reports:

```
main suite:  311 total, 0 failed, 270 succeeded, 41 skipped
charts:       12 total, 0 failed,   9 succeeded,  3 skipped
```

Those 44 skips are every test that needs HITRAN data reporting itself inconclusive rather than
failing. A passing build is therefore direct evidence that the no-data path still works — a
guarantee that was previously only ever checked by hand.

---

## Results at the default configuration

80 segments over 50 km, $S_0 = 1361$ W m⁻², albedo 0.30, column optical depth 1.8,
$\varepsilon_s = 0.98$, $v = 3$ m s⁻¹:

```
emission temperature      :    254.578 K
surface temperature       :    286.797 K   (13.65 C)
near-surface air          :    284.816 K
greenhouse warming        :     32.218 K
convecting layer top      :       3.44 km
outgoing longwave (TOA)   :    238.175 W/m2   (= absorbed solar, to 1e-6)
surface emission          :    375.953 W/m2
surface downward longwave :    230.638 W/m2
surface sensible heat     :     35.849 W/m2
```

| Convection | $T_s$ [K] | lapse 0–10 km [K km⁻¹] |
|---|---|---|
| none (pure radiative) | 290.97 | 5.19 |
| surface flux only | 289.78 | 5.06 |
| full | 286.80 | 4.75 |

Doubling the absorber gives an instantaneous forcing of 46.6 W m⁻² and a 27.4 K warming,
i.e. a climate sensitivity of 0.59 K per W m⁻². These are the right order for a grey model:
a single-band absorber overstates the forcing per doubling badly, because real greenhouse
gases saturate in their band centres.

### CO₂ concentration runs

`--co2-ppm` scales the CO₂ share of the dry absorber linearly with concentration, which is
the correct scaling for optical depth — it is the *forcing* that is logarithmic, and that
has to emerge from the model rather than be put in by hand. `--co2-scenario` runs a list of
concentrations and reports the instantaneous forcing of each step against the one before
it, with the baseline temperatures held so that forcing and feedback stay separate.

Run raw, the grey model is not usable for this. From 285 to 425 ppm:

```
dotnet run --project src/ClimateColumn.Cli -- --co2-scenario 285,425
```

| CO₂ [ppm] | dry τ | $T_s$ [K] | Δ$T$ [K] | forcing [W m⁻²] |
|---|---|---|---|---|
| 285 | 1.800 | 286.797 | – | – |
| 425 | 2.684 | 301.140 | **+14.343** | 28.538 |

The forcing is 28.5 W m⁻² where the accepted value is $5.35\ln(425/285) = 2.14$, so the
+14.3 K is meaningless. Diagnosing *which* part is wrong is worth doing, because it is not
the part usually blamed. Successive doublings in this model give 54.3 then 28.8 W m⁻² — the
forcing does saturate with concentration, just too aggressively (the real gas is nearly
constant per doubling). **The failure is one of magnitude, about a factor of 13, not of
shape.**

That distinction matters because a magnitude error can be calibrated out without distorting
the concentration dependence much. Two knobs do it: a spectral window, which suppresses every
forcing by roughly the share of the Planck function it removes, and `--co2-fraction`, which
says only part of the opacity is CO₂ in the first place. The second is the better choice here — a window large enough to
fix the magnitude also freezes the column, whereas the CO₂ share leaves the base state
alone.

#### What `--co2-fraction` means

The dry absorber stands for every well-mixed greenhouse gas, not CO₂ alone.
`--co2-fraction f` says how much of it is CO₂ **at the reference concentration**; the
remainder is CH₄, N₂O and the rest, and never moves. Only the CO₂ part is scaled by
concentration, so with $r = C/C_\text{ref}$,

$$\tau_\text{dry}(C) = \tau_\text{dry}(C_\text{ref})\left[(1-f) + f\,r\right].$$

It is a fixed input describing the reference state, not a quantity that tracks the run —
raising `--co2-ppm` does not change it. What *does* grow is the share CO₂ actually holds,
since its component grows while the others do not:

$$\text{realised share} = \frac{f\,r}{(1-f) + f\,r}.$$

At $f = 0.06$ that is 6.0 % of the dry absorber at 285 ppm and 8.7 % at 425 ppm, the total
going 1.800 → 1.853 with the non-CO₂ part pinned at 1.692 throughout. Set it once from the
composition of the reference state and leave it alone across a scenario: the growth is
already carried by $r$, so moving $f$ as well would count it twice.

To calibrate, sweep $f$ until the forcing `--co2-scenario` reports matches one you trust.
The two runs below both target $5.35\ln(425/285) = 2.14$ W m⁻² but need different $f$ — 0.06
and 0.11 — because they sit on different base states. The second carries more total opacity
(dry τ 2.0 plus a vapour component), so its band is nearer saturation and each increment of
CO₂ buys less: about 19 W m⁻² per unit $f$ against 35 for the first. $f$ is a property of
the configuration you calibrate it in, not a transferable constant.

```
dotnet run --project src/ClimateColumn.Cli -- --co2-fraction 0.06 --co2-scenario 285,425
```

| CO₂ [ppm] | $T_s$ [K] | Δ$T$ [K] | forcing [W m⁻²] | d$T$/d$F$ |
|---|---|---|---|---|
| 285 | 286.797 | – | – | – |
| 425 | 287.712 | **+0.916** | 2.158 | 0.424 |

and again with the water vapour feedback on, retuned to the same present-day surface
temperature (dry τ 2.0, vapour τ 1.8, ozone layer and pressure broadening enabled):

```
dotnet run --project src/ClimateColumn.Cli -- --optical-depth 2.0 --wv-tau 1.8 \
  --ozone-fraction 0.3 --pressure-broadening 1 --co2-fraction 0.11 --co2-scenario 285,425
```

| CO₂ [ppm] | $T_s$ [K] | Δ$T$ [K] | forcing [W m⁻²] | d$T$/d$F$ |
|---|---|---|---|---|
| 285 | 287.032 | – | – | – |
| 425 | 288.454 | **+1.422** | 2.142 | 0.664 |

So the answer the model gives for 285 → 425 ppm is **+0.9 K without the water vapour
feedback and +1.4 K with it**, the feedback raising d$T$/d$F$ from 0.42 to 0.66 K per
W m⁻². For orientation, the IPCC central equilibrium sensitivity of 3 K per doubling is
0.81 K per W m⁻², which would give +1.7 K for the same forcing.

Read these as a demonstration that the model's *dynamics* are sound once it is given a
sensible forcing, not as an independent estimate of anything. The forcing was calibrated in,
not predicted, and both configurations are run cloud-free — clouds exist in the model but are
off by default, and a configuration calibrated without them cannot simply have them switched
on, for the reason set out under [Clouds](#clouds). Neither configuration has a real spectrum
or any feedback except water vapour.

#### The calibration is local — do not extrapolate it

Matching the forcing at one concentration does not make the model right at every other one,
because `--co2-fraction` fixes the *magnitude* at the cost of the *shape*. Making CO₂ a
small slice of the absorber leaves that slice growing linearly against a large fixed
remainder, so each doubling adds twice the absolute optical depth the last one did and the
forcing per doubling **grows** rather than holding constant. Measured against the fixed
285 ppm state, so that it is directly comparable to $5.35\ln(C/C_0)$:

| Relative to 285 ppm | Model [W m⁻²] | Accepted [W m⁻²] | Ratio |
|---|---|---|---|
| ×2 (570 ppm) | 4.32 | 3.71 | 1.17 |
| 1000 ppm (the chart's range) | 10.34 | 6.72 | 1.54 |
| ×4 (1140 ppm) | 12.18 | 7.42 | 1.64 |
| 2000 ppm | 22.32 | 10.42 | 2.14 |

Each doubling buys 4.3, then 7.9, then 13.1 W m⁻² where the real gas buys 3.71 every time.
The surface response inherits that error: over 285 → 2000 ppm the model warms **+10.7 K**
without the vapour feedback and **+13.2 K** with it, against **+4.4 K** and **+6.9 K** if
the accepted forcing is applied at each configuration's own d$T$/d$F$.

Note this is a *different* measurement from the "54.3 then 28.8 W m⁻²" above, which holds
the temperature profile fixed at the standard atmosphere and scales the whole absorber
($f = 1$). Both are legitimate; they answer different questions, and the stepwise forcings
`--co2-scenario` prints are a third — each is measured against the *previous* equilibrium,
so they must not be summed and compared with $5.35\ln(C/C_0)$.

Practically, the agreement is under 0.01 K at the 425 ppm calibration point, 0.36 K at
600 ppm, 1.75 K at 1000 ppm and 6.30 K at 2000 ppm. **Treat anything much past 600 ppm as
unsound** unless you recalibrate `--co2-fraction` at the concentration you actually care
about.

#### What the chart shows

`Co2Sweep.SpectralBands()` — six molecules in sixteen bands derived from their own line strengths, so
the CO₂ response comes out of the spectroscopy rather than being calibrated in. **This is the only
configuration plotted.** The calibrated grey ones are still swept, because the findings about them
above are real, but a grey curve beside the spectral one invited the figure to be read as a
comparison of two models rather than as one model against the forcing law it ought to follow.

The dashed curve is **not** from the line data, and the chart says so: it is the accepted
$5.35\ln(C/C_0)$ itself. Both curves are forcing in W m⁻², so nothing is converted and no
sensitivity is borrowed — they meet at 285 ppm because the forcing against the reference is zero
there by definition, not by calibration.

| relative to 285 ppm | model forcing | accepted | ratio |
|---|---|---|---|
| 350 ppm | 1.136 | 1.099 | 1.03 |
| 500 ppm | 2.990 | 3.007 | 0.99 |
| 700 ppm | 4.613 | 4.807 | 0.96 |
| 1000 ppm | 6.238 | 6.716 | **0.93** |

Fitting $F = A\ln(C/C_0)$ gives $A \approx 4.84$ W m⁻² per ln against the accepted 5.35, so the
model now runs about **0.9×** the accepted law and crosses it near 500 ppm.

**It did not always.** Before the far wings were corrected, this table read 1.28 → 1.33 and the
fitted coefficient was 6.9–7.1 — the model over-forced by a third, near-uniformly. The whole of
that error turned out to be one piece of missing spectroscopy, described under
[the sub-Lorentzian correction](#the-far-wings-and-the-sub-lorentzian-correction) below.

**What the correction cost is the clean logarithm.** The drift of the fitted coefficient across
the sweep went from 2–5 % to 8–10 %, and the ratio column above now slides 1.03 → 0.93 where it
used to sit almost flat. That is not a regression, and it is worth being clear about why: a pure
exponential far wing gives an absorbing width $W = 2a\ln(k_0u)$ and hence exactly $F \propto \ln u$.
The Lorentzian wing is the idealisation that produces a clean logarithm. Suppressing it with a χ
factor breaks that idealisation — **the model was more logarithmic when it was more wrong.**

Either way this beats the calibrated grey model, whose ratio grew from 1.17 to 2.14 across the
same span because its absorber was diluted rather than resolved.

The absorber amount is *exactly* linear in concentration, so the logarithm is produced entirely by
the band structure. That is the point of the exercise.

#### Clouds on the CO₂ chart

The charted configuration is cloud-free by default. `--clouds`, or the app's **Clouds: off/on**
button, runs the cloudy one instead - and the two are calibrated to the *same* 286.796 K base
state, by different gas loadings (15.869 clear, 5.142 cloudy). Without that, switching clouds on
would move the surface about 15 K and every difference read off the figures afterwards would be
that offset rather than the cloud.

| | absorber scale | $A$ | vs accepted | drift | ΔT at 1000 ppm |
|---|---:|---:|---:|---:|---:|
| Clear | 15.869 | 4.839 | 0.904× | 10.1 % | +4.35 K |
| 67 % cloud | 5.142 | 4.676 | 0.874× | 1.1 % | +3.45 K |

**A deck masks about 6 % of the CO₂ forcing** - it already blocks part of the upward flux the
extra CO₂ would have intercepted. The response is also much closer to a pure logarithm, but read
that with care: holding the base state fixed required dropping the gas loading by a factor of
three, so cloud and loading moved together and this does not separate them.

#### The far wings and the sub-Lorentzian correction

The Lorentz profile comes from the impact approximation: collisions instantaneous compared with
the time between them. That holds near line centre and fails far from it — at a detuning of tens
of cm⁻¹ the radiation is probing the collision itself, over times short enough that its finite
duration matters. Real CO₂ wings therefore fall off **faster** than Lorentzian.

That is not a detail here. This model's CO₂ forcing comes almost entirely from the far wings —
the band core is saturated, so only the wings can still respond to more gas — which is *why* the
response is logarithmic at all. The far-wing shape is the thing that sets the forcing
coefficient.

The correction is the three-segment exponential of Perrin & Hartmann (1989), multiplying the
Lorentz profile:

$$\chi(s) = \exp\!\left[-B_1(s-s_1) - B_2(s-s_2)^+ - B_3(s-s_3)^+\right],\quad s = |\nu-\nu_0|$$

with breakpoints at 3, 30 and 120 cm⁻¹, continuous at each by construction, and applied to
**CO₂ alone** — a χ factor is fitted to one band of one gas, so giving CO₂'s to water vapour
would be inventing spectroscopy rather than correcting it.

What it does, measured rather than assumed:

| | absorber scale | base $T_s$ | $A$ | vs accepted |
|---|---:|---:|---:|---:|
| Pure Lorentz wings | 14.578 | 286.796 K | 6.988 | 1.31× |
| χ factor, same loading | 14.578 | 285.109 K | 5.073 | 0.95× |
| χ factor, recalibrated | 15.869 | 286.796 K | 5.091 | 0.95× |

Two things are worth reading off that table. The correction removes essentially the entire
over-forcing — 1.31× to 0.95×. And **recalibrating barely moved it**, 5.073 to 5.091, which is
the reassurance that the coefficient is a property of the spectroscopy rather than of how much
gas was put in.

The loading did have to move: suppressing the wings removes absorption, so the base state cooled
1.7 K and the absorber scale had to come back up from 14.578 to 15.869 to restore it. Changing
the shape and the loading in one step would have confounded the two, which is why they are
measured separately above.

**A caveat that travels with these numbers.** The functional form is Perrin & Hartmann's; the
coefficients are representative values for the CO₂ ν₂ band near 296 K and have *not* been checked
against the original paper. Treat this as a correctly shaped correction rather than a faithful
reproduction of a published fit — and note that the residual 0.9× is well inside the uncertainty
those coefficients carry, alongside everything still missing (line mixing, temperature-scaled
line intensities, and the fact that 5.35 is a stratosphere-adjusted tropopause forcing while this
is an instantaneous one at the top of the atmosphere). The coefficients are constructor
parameters so that anyone holding the paper can substitute the exact values and re-run the
measurement.

Nothing here was tuned to reach 5.35. The coefficient is reported as it came out, which is the
only way the forcing stays a prediction rather than a calibration.

#### Why these resolution settings, and not others

Sixteen bands, sixteen g-points, a **400 cm⁻¹ wing cutoff** and an absorber scale of 15.869. Those
are not arbitrary — they came out of a convergence study (`artifacts/convergence-study.txt`), and it
found three things worth stating because none of them was expected.

**The wing cutoff matters most**, which in hindsight follows from where the logarithm comes from.
Truncating the wings discards exactly the far-wing absorption that makes the response logarithmic.
Widening 15 → 400 cm⁻¹ converges $A$; 800 moves it a further 4 %. That sensitivity is what
identified the far wings as the suspect, and the χ factor above is what it led to.

**The previous settings — 8 bands, 4 g-points, a 15 cm⁻¹ cutoff — got the right answer by error
cancellation.** They reported $A = 6.99$ against a then-converged 6.9–7.1 (both measured before the χ factor),
but only because truncated
wings and a coarse band split compensated. Widening the cutoff *alone* took that configuration to
$A = 9.35$, badly wrong. Two errors had to move together, and that fragility — not the value — is
why the settings changed.

**The absorber scale is resolution dependent.** It exists to put the base state at an Earth-like
surface, and the 13.0 that did so at the old resolution leaves the surface 2.4 K too cold here.
`SpectralCalibrationTests` bisects it back to 286.796 K, so resolution and calibration are not
allowed to change at the same time.

The honest residue: the drift - now 8-10 % with the χ factor, 2-5 % without it - does not fall
further at any resolution tested, so it is a real departure rather than a numerical artefact.

Read it as spectroscopy, not prediction. The absorber amounts are scaled to reach an Earth-like
present-day surface rather than taken from observed concentrations, and the continuum that closes
the window is added rather than derived. What the line data determines is the *structure* — which
bands exist, how opaque each is relative to the others, and the distribution of absorption inside
each — and that structure is what makes the concentration dependence come out nearly right.

The charts skip rather than fall back when the line lists are absent: a figure captioned as spectral
must not quietly show something else. The sweep is nine equilibria at sixteen bands with sixteen
g-points. It used to take about 217 seconds; the concentrations are mutually independent, so they
now run concurrently and it takes about 59.

#### Plotting it

The sweep is `Co2Sweep` in Core, and two front ends draw it.

`Co2ChartTests` sweeps both configurations, asserts each of the claims above, and writes
`artifacts/co2-response.html` — a self-contained page with the chart, a hover readout and the
full table. Every figure in it is generated from the sweep, so the chart cannot drift from
the model.

`ClimateColumn.Charts` is a WinForms viewer of the same data, showing **two linked figures
side by side**: the response chart, and the vertical temperature profile of whichever
concentration the pointer is over. Clicking pins a concentration so the pointer can go
elsewhere; a values grid below tracks both, and there is a light/dark toggle and a Save PNG
for each figure.

The response chart plots three quantities, cycled by one button: **forcing** in W m⁻² against
the accepted law, **warming ΔT** from the 285 ppm base case, and **absolute surface
temperature**. ΔT is the same curve as the last of those with its base state subtracted off,
and it is the more readable — absolute temperatures put the whole sweep in a 7 K band starting
near 287 K, leaving the eye to do the subtraction the question was asking for anyway. On the ΔT
view the water-vapour feedback is the whole picture: **+4.35 K against +2.33 K** at 1000 ppm,
both curves leaving zero together.

Every view marks its value at 580 ppm directly on each curve, because the figure is also saved
as a PNG for documents, where there is no pointer to hover with.

The pairing is the point. The response chart says the surface warms by 4.35 K at 1000 ppm;
only the profile says *where in the column* that came from — the convective top lifting from
4.17 to 5.83 km, the height at which the column reaches the emission temperature rising from
4.52 to 5.43 km, and the upper column cooling while the surface warms.

The profile figure also marks the convecting layer, the cloud deck where there is one, and the
ground **separately from the air on it** — those are different temperatures, and their
difference is what drives the sensible heat flux.

Both render headlessly, which is how the images in this section are produced:

```
dotnet run --project src/ClimateColumn.Charts -- --png artifacts/co2-response.png
```

```
--png PATH          render the response chart to a PNG and exit, no window
--profile-png PATH  render the vertical profile to a PNG and exit
--profile-ppm N     which concentration the profile is drawn at    (580)
--clouds            run the cloudy configuration instead of the clear one
--warming           plot ΔT from 285 ppm instead of forcing
--temperature       plot absolute surface temperature instead of forcing
--dark              use the dark palette for --png
--width N           PNG width in pixels   (1100, 620 for the profile)
--height N          PNG height in pixels  (700, 820 for the profile)
--hover PPM         draw the readout box at this concentration
```

`--hover` exists because the readout box is otherwise only reachable by moving a mouse, so
nothing automated could check it. It renders the same panel the live chart shows, which makes
that drawing path verifiable and gives documentation shots a way to include it.

---

## Usage

```
--segments N               number of segments                     (80)
--top-km X                 altitude of the column top, km         (50)
--solar X                  solar constant, W/m2                   (1361)
--albedo X                 all-sky planetary albedo               (0.30)
--cloud-fraction X         sky covered by cloud; 0 disables it    (0)
--cloud-base-km X          cloud base altitude, km                (1.0)
--cloud-top-km X           cloud top altitude, km                 (4.5)
--cloud-emissivity X       longwave emissivity of the deck        (0.90)
--clear-sky-albedo X       albedo of the cloud-free sky           (0.155)
--cloud-albedo X           albedo of the cloudy sky               (0.361)
--sw-atm-fraction X        share of absorbed solar taken by air   (0.22)
--surface-emissivity X     surface longwave emissivity            (0.98)
--optical-depth X          absorber loading as tau at D = 2       (1.8)
--optical-depth-scale X    multiplier on the above                (1.0)
--diffusivity X            two-stream factor D; 2 = Koenigsberger (2.0)
--co2-ppm X                CO2 concentration                      (285)
--co2-reference-ppm X      ppm at which --optical-depth applies   (285)
--co2-fraction X           CO2 share of dry absorber at ref ppm   (1.0)
--co2-scenario A,B,C       equilibrium at each ppm, with forcings
--window-from-um X         transparent window, short edge, um     (none)
--window-to-um X           transparent window, long edge, um      (none)
--continuum-tau X          water-vapour continuum in the window   (0)
--continuum-foreign X      foreign share of the continuum         (0.5)
--k-distribution SHAPE     grey | lognormal | exponential         (grey)
--k-width X                spread of k within the band; 0 = grey  (0)
--k-points N               g-points across the band               (16)
--pressure-broadening N    dry absorber ~ rho (p/p0)^N            (0)
--ozone-fraction X         share of atm. solar into ozone layer   (0)
--ozone-altitude-km X      Chapman layer peak altitude            (25)
--ozone-width-km X         Chapman layer scale height             (5)
--wv-tau X                 water vapour tau at 288.15 K, CC feedback (0)
--wv-scale-height-km X     water vapour scale height              (2)
--convection MODE          none | surface | full                  (full)
--wind X                   surface wind speed, m/s, for h_c       (3.0)
--moisture X               surface moisture availability, 0-1;    (0)
                           0 disables evaporation, 1 is open water
--humidity X               near-surface relative humidity, 0-1    (0.8)
--spherical                spherical shells, not plane-parallel   (off)
--variable-gravity         g falls as 1/r^2; geopotential grid    (off)
--toa-interception         intercept sunlight on the TOA disc     (off)
--planet-radius-km X       radius for the three above             (6371)
--lapse-rate X             critical lapse rate, K/km              (6.5)
--surface-heat-capacity X  J/m2/K                                 (4.18e7)
--max-steps N              iteration cap                          (500000)
--isothermal               start isothermal instead of US Std Atm
--csv PATH                 write the profile to a CSV file
--sensitivity F            also run with optical depth x F and report dT/dF
--compare-convection       run all three convection modes side by side
--grid-convergence         refine the grid 4x and report the convergence order
```

The CSV is long-format. Fluxes are interface quantities and temperatures are segment
quantities, so they are emitted on separate rows (`INTERFACE` / `SEGMENT`), each carrying
the altitude the quantity actually belongs to — plotting `flux_up_W_m2` against `z_m` is
therefore correct rather than displaced by half a segment.

### As a library

```csharp
var result = ColumnModel.RunToEquilibrium(new ModelOptions
{
    SegmentCount = 120,
    TotalOpticalDepth = 2.4,
    Convection = ConvectionMode.Full
});

Console.WriteLine(result.SurfaceTemperature);            // K
Console.WriteLine(result.Radiation.OutgoingLongwave);    // W/m2
Console.WriteLine(result.Radiation.KoenigsbergerEmission[0]);  // 4 eps' sigma T^4 dz
```

---

## Project layout

```
src/ClimateColumn.Core/
  PhysicalConstants.cs   constants, and the derivation of D = 2
  StandardAtmosphere.cs  US Standard Atmosphere 1976
  Segment.cs             one segment: geometry, mass, eps', Koenigsberger emission
  Column.cs              the segmented column, optical depth and solar distribution
  RadiationSolver.cs     the two-stream Schwarzschild recurrence
  ConvectionSolver.cs    h_c, sol-air temperature, lapse-rate adjustment
  ColumnModel.cs         adaptive explicit march to equilibrium
  GridConvergence.cs     refinement study with Richardson extrapolation
  Co2Sweep.cs            concentration sweep and instantaneous forcing
  SpectralBand.cs        one band: extent, absorbers, line structure
  BandDerivation.cs      derives a band set from resolved line data
  KDistribution.cs       correlated-k quadrature over a band
  LineByLine.cs          resolved-spectrum reference for checking the band approximations
  HitranLineList.cs      reads a downloaded HITRAN line list
  Reporting.cs           console tables and CSV
src/ClimateColumn.Cli/   command-line driver
src/ClimateColumn.Charts/  WinForms chart viewer and PNG export
tests/ClimateColumn.Tests/  MSTest suite (net8.0)
tests/ClimateColumn.Charts.Tests/  PNG export tests (net8.0-windows)
scripts/                 offline package-cache bootstrap
```

`Co2Sweep` lives in Core rather than in either front end, so the WinForms viewer, the CLI
and the test suite all plot the same numbers from the same code.

`ClimateColumn.Charts` and its test project target `net8.0-windows`; Core, the CLI and the
main test suite stay on plain `net8.0` and remain cross-platform. The Windows-only tests are
a separate project for exactly that reason — referencing the WinForms assembly would have
forced the whole suite onto a Windows target.

`ClimateColumn.Charts.Tests` covers what the Save PNG button does: a valid, correctly sized
file comes out, the theme and hover state reach the pixels, and the awkward cases behave — a
missing folder, an existing file, a window too small to draw in, an out-of-range hover index,
a save attempted before the sweep finished. It builds its sweeps from synthetic numbers rather
than running the model, so it finishes in about a quarter of a second; the physics is covered
in `ClimateColumn.Tests`. The file dialog itself is not driven: it is OS shell code, and a
modal window in a test run is a reliable source of hangs.

Build and test:

```
dotnet build ClimateColumn.sln -c Release
dotnet test ClimateColumn.sln -c Release
```

`nuget.config` clears every package source, so nothing reaches the network at build time.
`ClimateColumn.Core` and `ClimateColumn.Cli` have no package dependencies at all and build
that way from a cold cache. The test project uses MSTest, so on a new machine its packages
(11, 16.4 MB) have to reach the local NuGet cache once first:

```
pwsh scripts/populate-package-cache.ps1
```

After that, restore, build and test are all offline. For a genuinely air-gapped machine the
same script stages the `.nupkg` files on a connected one (`-Export`) and seeds the cache
from that folder (`-Source`); see [scripts/README.md](scripts/README.md).

---

## Known limitations

- **Grey, or at best two-band.** The window splits the spectrum into one grey band and one
  transparent one; that is still nothing like a real absorption spectrum. It is why the raw
  doubling forcing is an order of magnitude too large, and why the CO₂ runs above have to
  be calibrated against a known forcing rather than predicting one. The window's *share* now
  follows the Planck function, the window can close via the continuum, and the absorbing band
  can carry line structure — but it is still two bands, and the k-distribution's shape and
  width are prescribed rather than derived from line data.
- **The continuum's strength is tuned, not derived.** Its two scalings are physical, but the
  magnitude is one free parameter rather than fitted MT_CKD coefficients, and it is applied
  only inside the window rather than across the spectrum.
- **The parametric k-distribution does not fit real bands.** CO₂'s best lognormal width drifts
  from 1.70 to 1.25 across optical depth, and H₂O wants 2.40 where CO₂ wants 1.50 — so no single
  `--k-width` can serve an atmosphere containing both. Use `MeasuredKDistribution` when you have
  line data.
- **Line strengths are not temperature-scaled.** Half-widths *are*: HITRAN's `n_air` is applied
  as $\gamma = \gamma_{\text{ref}}\,(p/p_{\text{ref}})\,(296/T)^n$, so a stratospheric layer at
  0.1 atm and 217 K comes out ~12 % broader than pressure alone would give. Intensities are still
  used at their 296 K values, because scaling those needs total internal partition sums. Self
  broadening, Voigt profiles (Doppler matters in the upper stratosphere), line mixing, sub-Lorentzian
  far wings — which matter specifically for CO₂'s 15 µm wings, the band doing the work — and
  pressure shift are all absent.
- **Gas overlap is approximated.** A band's k-distribution comes from the total absorption of every
  gas in it at reference amounts, so moving one gas far from its reference degrades the distribution.
  Re-derive rather than extrapolate.
- **Band count and the absorber amounts are still chosen.** Everything else follows from the
  spectrum, but how finely to band and how much of each gas to put in it do not — and the amounts
  used in the README are illustrative, not fitted to observations.
- **The spectral chart series is scaled, not fitted.** Its absorber amounts reach an Earth-like base
  state by construction rather than from observed concentrations, and its continuum is added rather
  than derived, so its CO₂ response is spectroscopically grounded but not an independent estimate.
- **The forcing comparison borrows nothing, but it only tests the forcing.** Both plotted curves are
  W m⁻², so no sensitivity is used to convert either. The trade is that the chart says nothing about
  whether the *temperature* response is right: the configuration's own d$T$/d$F$ is 0.729 K per
  W m⁻², and nothing here checks it against anything.
- **The ~3.6 % logarithmic drift is unexplained.** It does not fall at any resolution tested — more
  g-points, more bands and a wider wing cutoff all leave it between 2 % and 5 % — so it is not a
  numerical artefact, but nor has it been traced to a mechanism.
- **Shortwave is still a single grey channel.** All the spectral work is on the longwave side;
  solar absorption is a prescribed fraction split by air mass and a Chapman profile.
- **The diffusivity is band-independent.** $D = 2$ is exact in the optically *thin* limit,
  which makes it the worst choice for opaque band centres. One $D$ cannot be right for bands
  spanning $\tau \ll 1$ to $\tau \gg 1$; the exact $2E_3(\tau)$ transmission would be needed.
- **The *column* is not checked against a line-by-line reference.** The band approximations are —
  against both a synthetic spectrum and real HITRAN lines — but no run of the full column is
  compared with a resolved calculation, so nothing here says the emergent spectrum resembles
  Earth's.
- **The water vapour feedback is crude.** It is on by loading only: a single column-integrated
  $\tau_v$ following Clausius–Clapeyron on the near-surface air temperature, with a fixed
  vertical shape and no relative humidity profile, no advection, and no distinction between
  the boundary layer and the free troposphere. It is off by default, and configurations that
  use it can sit uncomfortably close to runaway.
- **One grey cloud deck.** Clouds now have both an albedo and a longwave opacity, but a single
  deck at one height with one emissivity stands in for everything from fog to cirrus, and it is
  prescribed rather than predicted — nothing forms or dissipates it, so it cannot act as a
  feedback. Its droplets scatter no shortwave explicitly; the reflection is an albedo, not a
  radiative transfer.
- **No scattering**, no diurnal or seasonal cycle, and a globally averaged solar input ($S_0/4$).
- **No feedbacks other than water vapour.** No ice–albedo, no lapse-rate feedback beyond
  what the fixed critical lapse rate already imposes, and a fixed planetary albedo.
- **The ozone layer is a heating profile, not chemistry.** It deposits a prescribed share of
  the solar absorption on a Chapman shape; nothing produces or destroys ozone, and it has no
  longwave effect.
- **The convective adjustment is a hard constraint**, not a mass-flux scheme, and conserves
  dry enthalpy rather than moist static energy. Latent heat now enters the *surface* budget,
  but there is no prognostic humidity, so nothing transports moisture aloft: the condensation
  heating is delivered at the base of the convecting layer and mixed from there. Conserving
  $h = c_p T + gz + Lq$ properly would need a moisture variable.
- **Evaporation is off by default and cannot reach Earth's magnitude.** See the latent-heat
  section: $\beta \approx 0.35$ matches Earth's Bowen ratio but the total turbulent flux tops
  out near 41 W m⁻² against ~100 observed, because $h_c$ is a film coefficient rather than a
  bulk transfer coefficient. Beyond $\beta \approx 0.5$ the sensible flux goes negative.
- **Local thermodynamic equilibrium is assumed** — the source function is the Planck function
  everywhere. True below about 60 km, so the 50 km model top keeps it safe, but it is an
  assumption rather than a result.
- **Sphericity and variable gravity are off by default, and partial when on.** `--spherical`
  adds the shell-area and $-(2/r)F$ terms; `--variable-gravity` adds the inverse-square law and
  the geopotential grid; `--toa-interception` adds the limb annulus. What still does not follow:
  the limb calculation's shortwave extinction is inferred from the prescribed absorption rather
  than given, and reading that absorption as slant rather than vertical would raise the limb
  capture; the shell factor is held constant across each segment rather than integrated within it;
  the dry adiabat's
  1.55 % relaxation does not reach the convection scheme, which runs on a prescribed critical
  lapse rate; and the exterior field is used throughout, ignoring the air mass below a given
  level, which by the shell theorem would add to the enclosed mass. Horizontal transport is
  absent in every geometry — this is a single column, not a sphere of columns.
- **The angular integral is collapsed onto one number.** The exact hemispheric flux is
  $2\pi\int_0^1 I(z,\mu)\,\mu\,\mathrm{d}\mu$; the solver replaces it with a single $D$. The
  exact $2E_3(\tau)$ transmission exists in the test suite as a reference and is never used by
  the solver.
- The layer mass grid is built once from the standard atmosphere and held fixed as the
  temperature profile evolves.

## Licence

The code in this repository is MIT licensed — see [LICENSE](LICENSE).

**The HITRAN line data is not.** It is third-party data with its own terms and its own citation
requirement, it is deliberately not committed here, and the MIT licence above does not extend to
it. `scripts/fetch-hitran.ps1` downloads it from hitran.org at your request; if you publish
anything derived from it, cite HITRAN rather than this repository.

The U.S. Standard Atmosphere 1976 coefficients in `StandardAtmosphere.cs` are published reference
values, and the Koenigsberger relations are from the cited textbook.
