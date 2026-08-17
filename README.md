# ClimateColumn

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

Two non-radiative terms, both switchable via `--convection none|surface|full`:

- **Surface sensible heat**, $Q_c = h_c\,(T_s - T_{\text{air}})$, with the wind-speed
  relation $h_c = 5.8 + 4.1\,v$ from Koenigsberger et al., *Manual of Tropical Housing and
  Building*. The air temperature is extrapolated to $z=0$ from the two lowest segments.
- **Critical-lapse-rate adjustment** on the atmospheric segments: wherever the lapse rate
  exceeds 6.5 K km⁻¹, the affected block is mixed onto exactly that lapse rate while
  conserving $\sum C_i T_i$.

The surface is deliberately **excluded** from the adjustment. It exchanges heat with the
air only through radiation and $h_c$; letting the adjustment mix the surface reservoir as
well would double count that transfer and leave the surface energy budget open.

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
`dotnet test` and `dotnet run` work against it. 113 cases. The ones that matter are checks
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

The suite is organised by what it constrains — `AnalyticBenchmarkTests` and
`SolverConsistencyTests` hold the assertions above, `ExtendedPhysicsTests` and `Co2Tests`
the optional physics, `FullModelTests` the end-to-end budgets. Equilibrium runs are the
expensive part and several tests need the same configurations, so `TestSupport.Equilibrium`
memoises them; the whole suite takes about 5 seconds.

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
not predicted, and both configurations still lack clouds, a real spectrum, and every
feedback except water vapour.

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

#### Plotting it

The sweep is `Co2Sweep` in Core, and two front ends draw it.

`Co2ChartTests` sweeps both configurations, asserts each of the claims above, and writes
`artifacts/co2-response.html` — a self-contained page with the chart, a hover readout and the
full table. Every figure in it is generated from the sweep, so the chart cannot drift from
the model.

`ClimateColumn.Charts` is a WinForms viewer of the same data: the chart, a values grid that
tracks the pointer, a light/dark toggle and Save PNG. It also renders headlessly, which is
how the images in this section are produced:

```
dotnet run --project src/ClimateColumn.Charts -- --png artifacts/co2-response.png
```

```
--png PATH        render straight to a PNG and exit, no window
--dark            use the dark palette for --png
--width N         PNG width in pixels   (1100)
--height N        PNG height in pixels  (700)
--hover PPM       draw the readout box at this concentration
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
--albedo X                 planetary albedo                       (0.30)
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
- **The k-distribution is not correlated across levels from real data.** Correlated-k assumes
  the ordering of $k$ holds at every pressure and temperature; here that is assumed rather than
  checked against line-by-line calculations, and the same distribution is used at every level.
- **The diffusivity is band-independent.** $D = 2$ is exact in the optically *thin* limit,
  which makes it the worst choice for opaque band centres. One $D$ cannot be right for bands
  spanning $\tau \ll 1$ to $\tau \gg 1$; the exact $2E_3(\tau)$ transmission would be needed.
- **Nothing is checked against a line-by-line reference.** The window benchmarks verify the
  algebra and the Planck integral, not whether the resulting spectrum resembles Earth's.
- **The water vapour feedback is crude.** It is on by loading only: a single column-integrated
  $\tau_v$ following Clausius–Clapeyron on the near-surface air temperature, with a fixed
  vertical shape and no relative humidity profile, no advection, and no distinction between
  the boundary layer and the free troposphere. It is off by default, and configurations that
  use it can sit uncomfortably close to runaway.
- **No clouds** — neither their albedo nor their longwave opacity — and no scattering,
  no diurnal or seasonal cycle, and a globally averaged solar input ($S_0/4$).
- **No feedbacks other than water vapour.** No ice–albedo, no lapse-rate feedback beyond
  what the fixed critical lapse rate already imposes, and a fixed planetary albedo.
- **The ozone layer is a heating profile, not chemistry.** It deposits a prescribed share of
  the solar absorption on a Chapman shape; nothing produces or destroys ozone, and it has no
  longwave effect.
- **The convective adjustment is a hard constraint**, not a mass-flux scheme, and conserves
  enthalpy rather than moist static energy.
- The layer mass grid is built once from the standard atmosphere and held fixed as the
  temperature profile evolves.
