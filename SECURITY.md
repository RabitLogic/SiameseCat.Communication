# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| 1.0.x | ✅ |

Security fixes are released as patch versions on the latest minor release.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private vulnerability reporting on
<https://github.com/RabitLogic/SiameseCat.Communication/security/advisories/new>, or email the maintainers listed in
the repository profile. Include:

- affected package and version,
- a minimal reproduction (configuration, address, and the frame or value involved),
- impact assessment (for example denial of service through a malformed frame, or credentials leaking
  into logs).

You can expect an initial response within 5 working days. Once a fix is available it is released as a
patch version together with a GitHub Security Advisory; credit is given unless you prefer to stay
anonymous.

## Scope

In scope:

- the `SiameseCat.Communication.*` NuGet packages,
- protocol handling that processes untrusted input (Modbus slaves, frame parsing, address parsing).

Out of scope:

- misconfiguration of a real device or network (for example exposing a Modbus slave to the internet),
- vulnerabilities in third-party runtimes or in the device firmware itself.

## Hardening notes

- `ModbusTcpSimulator` listens on `127.0.0.1` by default; bind to a routable address only in a trusted
  network.
- `ModbusRtuSimulator` and the Modbus drivers reject malformed frames (bad CRC, illegal length) instead of
  acting on them.
- `DriverConfiguration.LogFrames` writes raw frames to the logger at `Trace` level; keep it disabled in
  production if frames may contain sensitive process data.
