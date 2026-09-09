# Jetson integration boundary

`DEV-10` supplies only a C# fixture protocol and simulated controller. This directory contains no listener, process wrapper, GPIO access, network adapter, serial adapter, LED control, E-stop control, or copy of the existing ELTS controller.

The fixture schema is [`../schemas/protocol/elts-development-synthetic-v1.schema.json`](../schemas/protocol/elts-development-synthetic-v1.schema.json). It follows the concepts in specification section 11, but its explicit field names, `development-synthetic-v1` version, and TimeSpan-tick representation are development conventions. It is **not** asserted compatible with the built controller.

Before a real wrapper can be built, the ELTS code owner must provide the controller source or reviewed wire contract, the actual LED-permission feedback point, and the selected D-03 transport. D-10 must select study behavior for NE blocks, and investigators must decide D-13 before a real exposure/max-on-time implementation. Physical link timing, shutdown, E-stop, and light behavior require the unavailable test equipment and bench evidence.
