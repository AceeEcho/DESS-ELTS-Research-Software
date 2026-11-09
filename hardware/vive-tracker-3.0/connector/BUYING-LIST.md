# Revision A buying list — one DigiKey order

Checked October 14, 2026 (US Central). Replaces the earlier multi-store list. Nothing has been ordered. All linked items showed DigiKey stock when checked; stock and checkout prices can change. Assumes you already have PETG and assembly tools.

| Order quantity | Item and direct DigiKey link | Approximate line cost |
|---:|---|---:|
| 10 | [Mill-Max 1946-0-00-15-00-00-03-0 contacts / ED1360-ND](https://www.digikey.com/en/products/detail/mill-max-manufacturing-corp/1946-0-00-15-00-00-03-0/5176096) — only 2 needed, 8 spares | $3.01 |
| 1 | [Keystone 21908, 1/4-20 × 1/2-inch titanium socket-head screw](https://www.digikey.com/en/products/detail/keystone-electronics/21908/13968037) — head-height note below | $5.18 |
| 2 | [Keystone 29314, M3 × 12 mm steel slotted pan-head screws](https://www.digikey.com/en/products/detail/keystone-electronics/29314/1532209) | $1.08 |
| 2 | [Keystone 4708, M3-0.5 steel hex nuts](https://www.digikey.com/en/products/detail/keystone-electronics/4708/4499301) | $0.22 |
| 1 spool | [Adafruit 2517, 26 AWG black silicone wire, 25 ft](https://www.digikey.com/en/products/detail/adafruit-industries-llc/2517/5975015) — cut two lengths and label trigger | $4.95 |
| 1 piece | [Sumitomo K 3/32 CLEAR 4FT thin-wall heat-shrink](https://www.digikey.com/en/products/detail/sumitomo-electric-interconnect-products-inc/K-3-32-CLEAR-4FT/13170857) — individual solder joints | $4.37 |
| 1 foot | [Sumitomo B2 3/16 BLACK SPL heat-shrink](https://www.digikey.com/en/products/detail/sumitomo-electric-interconnect-products-inc/B2-3-16-BLACK-SPL/13170722) — cable-exit sleeve; select cut length/Digi-Spool, not full spool | About $1.20 |
| 2 | [Panduit PLT1M-C, 2.5 mm cable ties](https://www.digikey.com/en/products/detail/panduit-corp/PLT1M-C/280033) — 1 needed, 1 spare | $0.50 |
| 1, optional | [SS-01GL miniature lever switch with solder terminals](https://www.digikey.com/en/products/detail/aratas-formerly-omron-components/SS-01GL/369830) — COM and NO only | $4.23 |

About **$21 before shipping/tax**, or **$25 including the optional switch**. The switch is for wiring/bench use; its fit into the eventual trigger mechanism has not been designed. Reuse an existing suitable normally open trigger switch if you have one. The [parts CSV](DigiKey-parts.csv) contains the eight required purchase lines.

## Substitutions and fit

The exact Mill-Max contacts are unchanged: flat gold targets, with the tracker supplying the spring-loaded side. Do not substitute the concave 1945 part or generic spring-loaded pogo pins.

The **Keystone 21908** has the same 12.7 mm under-head length as the original mounting screw. Its Ø9.53 mm head clears the existing Ø11.8 mm recess. The taller 6.35 mm head projects **2.55 mm below the body**, or **0.55 mm below the nearby cover's flat face**. Thread projection into the tracker remains **4.5 mm**. A CAD envelope check found zero intersection with all four printed parts. Use no washer. The existing STEP/STLs need no modification. Physical fit remains untested. Titanium is selected because this particular screw is stocked by the same supplier; titanium is not required.

The **29314 cover screws** use a slotted screwdriver rather than the original hex key. Their M3 thread and 12 mm length are unchanged; Ø5.4 × 1.7 mm external heads fit the cover. The 4708 nuts are 5.51 mm across flats and 2.39 mm thick, within the existing pockets. No washers.

The cheaper [80/20 3061 button-head screw](https://www.digikey.com/en/products/detail/80-20-llc/3061/21320892) also matches the original design but showed a two-week manufacturer lead time, so it is not the choice for arrival within a week.

## Insulation

The clear contact tubing has 2.36 mm supplied ID and 0.25 mm nominal recovered wall thickness. Keep solder joints narrow; verify each finished insulated joint is at most 2.3 mm OD before inserting it. Its PVDF material needs a listed 170°C shrink temperature: **shrink on the loose contact/wire assemblies away from printed parts and the tracker**, then cool before fitting. The selected silicone wire is rated to 200°C. Actual joint dimensions still need checking.

The larger black tubing sleeves both wires at the exit. Keep the sleeved pair within the 4.4 × 3.5 mm opening and put the cable tie around that sleeve. See [assembly instructions](README.md).

## Delivery and supplies

The selected items showed DigiKey stock, not third-party Marketplace offers. Representative stock: 100 mounting screws, 2,464 cover screws, 35,504 nuts, 180 wire spools and 1,404 clear tubing pieces. Stock is not reserved.

DigiKey lists [US UPS/FedEx two-day air at $13.99](https://www.digikey.com/en/help-support/delivery-information/delivery-time-and-cost). Stocked items plus that service make arrival within a week reasonable, not guaranteed. Estimate **$35 before tax without the switch**, or **$39 with it**, including that shipping option. Checkout may add tariffs. No ZIP-code delivery investigation was performed.

PETG, soldering supplies, a suitable heat gun, continuity meter, slotted screwdriver and matching mounting-screw hex key are general workshop supplies excluded from these totals.
