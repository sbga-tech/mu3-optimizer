#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import secrets
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path

UNITY_VERSION = "5.6.4f1"
UNITY_EDITOR_USER_AGENT = (
    "UnityEditor/5.6.4.11300998 (Windows; U; Windows NT 10.0; en)"
)
UNITY_WEB_USER_AGENT = (
    "Mozilla/5.0 (Windows; Win64; ) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/37.0.2062.94 Safari/537.36 "
    "Unity/5.6.4f1 (unity3d.com;light)"
)


class ActivationError(RuntimeError):
    pass


@dataclass(frozen=True)
class Endpoints:
    core: str = "https://core.cloud.unity3d.com"
    license: str = "https://license.unity3d.com"
    activation: str = "https://activation.unity3d.com"

DEFAULT_ENDPOINTS = Endpoints()


@dataclass(frozen=True)
class UnityMachineInfo:
    iso_code: str
    user_name: str
    operating_system: str
    operating_system_numeric: int
    processor_type: str
    processor_speed: int
    processor_count: int
    processor_cores: int
    physical_memory_mb: int
    computer_name: str
    computer_model: str
    bindings: dict[str, str]


def _decode_windows_output(data: bytes) -> str:
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16")
    if data and data[1::2].count(0) > len(data) // 4:
        return data.decode("utf-16-le")
    return data.decode("utf-8", errors="strict")


def _public_environment() -> dict[str, str]:
    environment = os.environ.copy()
    environment.pop("UNITY_EMAIL", None)
    environment.pop("UNITY_PASSWORD", None)
    environment["LC_ALL"] = "C.UTF-8"
    return environment


class WineMachineCollector:
    def __init__(
        self,
        runner: Callable[..., subprocess.CompletedProcess[bytes]] = subprocess.run,
    ) -> None:
        self.runner = runner

    def _run(self, arguments: Sequence[str]) -> None:
        try:
            result = self.runner(
                list(arguments),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
                env=_public_environment(),
            )
        except OSError as error:
            raise ActivationError(f"could not run {arguments[0]}: {error}") from error
        if result.returncode != 0:
            detail = _decode_windows_output(result.stderr or result.stdout).strip()
            raise ActivationError(
                f"{' '.join(arguments)} failed with exit {result.returncode}"
                + (f": {detail}" if detail else "")
            )

    @staticmethod
    def _wine_path(path: Path) -> str:
        return "Z:" + str(path.resolve()).replace("/", "\\")

    def _collect_outputs(self) -> dict[str, str]:
        with tempfile.TemporaryDirectory(prefix="unity-machine-") as directory:
            root = Path(directory)
            outputs = {
                name: root / f"{name}.txt"
                for name in (
                    "windows",
                    "locale",
                    "processor",
                    "bios",
                    "manufacturer",
                    "model",
                    "memory",
                    "processor_count",
                    "processor_cores",
                    "environment",
                )
            }

            def redirect(command: str, output: str) -> str:
                return f'{command} > "{self._wine_path(outputs[output])}" 2>&1'

            commands = [
                "@echo off",
                redirect(
                    'reg query "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"',
                    "windows",
                ),
                redirect(
                    'reg query "HKCU\\Control Panel\\International" /v LocaleName',
                    "locale",
                ),
                redirect(
                    'reg query "HKLM\\HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0"',
                    "processor",
                ),
                redirect("wmic bios get SerialNumber", "bios"),
                redirect("wmic computersystem get Manufacturer", "manufacturer"),
                redirect("wmic computersystem get Model", "model"),
                redirect("wmic computersystem get TotalPhysicalMemory", "memory"),
                redirect("wmic cpu get NumberOfLogicalProcessors", "processor_count"),
                redirect("wmic cpu get NumberOfCores", "processor_cores"),
                f'echo UserName=%USERNAME% > "{self._wine_path(outputs["environment"])}"',
                f'echo ComputerName=%COMPUTERNAME% >> "{self._wine_path(outputs["environment"])}"',
            ]
            batch = root / "collect.bat"
            batch.write_bytes(("\r\n".join(commands) + "\r\n").encode("ascii"))
            self._run(["wine", "cmd", "/d", "/c", self._wine_path(batch)])
            return {
                name: _decode_windows_output(path.read_bytes())
                for name, path in outputs.items()
            }

    @staticmethod
    def _registry_values(output: str) -> dict[str, str]:
        pattern = re.compile(
            r"^\s*(\S+)\s+REG_[A-Z0-9_]+\s+(.*?)\s*$",
            re.MULTILINE,
        )
        return {match.group(1): match.group(2) for match in pattern.finditer(output)}

    @staticmethod
    def _wmic_value(output: str, property_name: str) -> str:
        lines = [line.strip().lstrip("\ufeff") for line in output.splitlines()]
        values = [line for line in lines if line and line != property_name]
        if not values:
            raise ActivationError(f"Wine WMI value not found: {property_name}")
        return "".join(sorted(values))

    @staticmethod
    def _required(values: Mapping[str, str], name: str) -> str:
        value = values.get(name)
        if value is None or not value.strip():
            raise ActivationError(f"Wine machine value not found: {name}")
        return value.strip()

    @staticmethod
    def _processor_name(value: str) -> str:
        output = ""
        for index, character in enumerate(value):
            if not character.isspace():
                output += character
                continue
            if not output:
                continue
            if index != len(value) - 1 and value[index + 1].isspace():
                continue
            output += character
        return output

    @staticmethod
    def _windows_name(major: int, minor: int) -> str:
        names = {
            (5, 0): "Windows 2000",
            (5, 1): "Windows XP",
            (5, 2): "Windows 2003 Server",
            (6, 0): "Windows Vista",
            (6, 1): "Windows 7",
            (6, 2): "Windows 8",
            (6, 3): "Windows 8.1",
            (10, 0): "Windows 10",
        }
        return names.get((major, minor), "unknown Windows version")

    def collect(self) -> UnityMachineInfo:
        outputs = self._collect_outputs()
        windows = self._registry_values(outputs["windows"])
        locale = self._registry_values(outputs["locale"])
        processor = self._registry_values(outputs["processor"])
        environment = dict(
            line.strip().split("=", 1)
            for line in outputs["environment"].splitlines()
            if "=" in line
        )

        current_version = self._required(windows, "CurrentVersion")
        major_text, minor_text = current_version.split(".", 1)
        major = int(major_text)
        minor = int(minor_text)
        build = int(self._required(windows, "CurrentBuildNumber"))
        service_pack = self._required(windows, "CSDVersion")
        os_name = self._windows_name(major, minor)
        operating_system = (
            f"{os_name} {service_pack} ({major}.{minor}.{build}) 64bit"
            if service_pack
            else f"{os_name}  ({major}.{minor}.{build}) 64bit"
        )

        bios_identifier = self._wmic_value(outputs["bios"], "SerialNumber")
        manufacturer = self._wmic_value(outputs["manufacturer"], "Manufacturer")
        model = self._wmic_value(outputs["model"], "Model")
        computer_model = model + (f" ({manufacturer})" if manufacturer else "")

        bindings = {
            "1": self._required(windows, "ProductId"),
            "4": base64.b64encode(bios_identifier.encode("utf-8")).decode("ascii"),
        }
        # Unity only emits binding 5 on Windows 10+. Wine 8 in this pinned image
        # reports Windows 7. Its storage-property query also fails, so binding 2
        # is intentionally absent, exactly as in Unity's native ALF.

        return UnityMachineInfo(
            iso_code=self._required(locale, "LocaleName").split("-", 1)[0].lower(),
            user_name=self._required(environment, "UserName"),
            operating_system=operating_system,
            operating_system_numeric=100 * major + 10 * minor,
            processor_type=self._processor_name(
                self._required(processor, "ProcessorNameString")
            ),
            processor_speed=int(self._required(processor, "~MHz"), 0),
            processor_count=int(
                self._wmic_value(outputs["processor_count"], "NumberOfLogicalProcessors")
            ),
            processor_cores=int(
                self._wmic_value(outputs["processor_cores"], "NumberOfCores")
            ),
            physical_memory_mb=int(
                self._wmic_value(outputs["memory"], "TotalPhysicalMemory")
            )
            >> 20,
            computer_name=self._required(environment, "ComputerName"),
            computer_model=computer_model,
            bindings=bindings,
        )


XML_ATTRIBUTES = "@attributes"
XML_DECLARATION = b'<?xml version="1.0" encoding="UTF-8"?>'


def dict_to_xml(tag: str, data: object) -> ET.Element:
    attributes: dict[str, str] = {}
    if isinstance(data, Mapping):
        raw_attributes = data.get(XML_ATTRIBUTES, {})
        if not isinstance(raw_attributes, Mapping):
            raise TypeError(f"{tag} XML attributes must be a mapping")
        attributes = {
            str(name): str(value) for name, value in raw_attributes.items()
        }

    element = ET.Element(tag, attributes)
    if not isinstance(data, Mapping):
        if data is not None:
            element.text = str(data)
        return element

    for child_tag, child_data in data.items():
        if child_tag == XML_ATTRIBUTES:
            continue
        if not isinstance(child_tag, str):
            raise TypeError(f"{tag} XML child names must be strings")
        children = child_data if isinstance(child_data, list) else [child_data]
        for child in children:
            element.append(dict_to_xml(child_tag, child))
    return element


def machine_id_for_bindings(bindings: dict[str, str]) -> str:
    missing = sorted({"1", "4"} - bindings.keys())
    if missing:
        raise ActivationError(f"machine binding(s) missing: {', '.join(missing)}")
    material = bindings["1"] + bindings.get("2", "") + bindings["4"]
    return base64.b64encode(hashlib.sha1(material.encode("utf-8")).digest()).decode(
        "ascii"
    )


def system_info_data(machine: UnityMachineInfo) -> dict[str, object]:
    return {
        "IsoCode": machine.iso_code,
        "UserName": machine.user_name,
        "OperatingSystem": machine.operating_system,
        "OperatingSystemNumeric": machine.operating_system_numeric,
        "ProcessorType": machine.processor_type,
        "ProcessorSpeed": machine.processor_speed,
        "ProcessorCount": machine.processor_count,
        "ProcessorCores": machine.processor_cores,
        "PhysicalMemoryMB": machine.physical_memory_mb,
        "ComputerName": machine.computer_name,
        "ComputerModel": machine.computer_model,
        "UnityVersion": UNITY_VERSION,
    }


def build_alf(machine: UnityMachineInfo) -> bytes:
    bindings = [
        {
            XML_ATTRIBUTES: {
                "Key": key,
                "Value": machine.bindings[key],
            }
        }
        for key in ("1", "2", "4", "5")
        if key in machine.bindings
    ]
    root = dict_to_xml(
        "root",
        {
            "SystemInfo": system_info_data(machine),
            "License": {
                XML_ATTRIBUTES: {"id": "Terms"},
                "MachineID": {
                    XML_ATTRIBUTES: {
                        "Value": machine_id_for_bindings(machine.bindings)
                    }
                },
                "MachineBindings": {"Binding": bindings},
                "UnityVersion": {
                    XML_ATTRIBUTES: {"Value": UNITY_VERSION}
                },
            },
        },
    )
    return XML_DECLARATION + ET.tostring(
        root, encoding="utf-8", short_empty_elements=True
    )


def build_return_payload(ulf: bytes, machine: UnityMachineInfo) -> bytes:
    root = _parse_xml(ulf, "ULF")
    if _local_name(root) != "root":
        raise ActivationError("ULF does not contain a root element")
    root_start = ulf.find(b"<root")
    root_end = ulf.find(b">", root_start)
    if root_start < 0 or root_end < 0:
        raise ActivationError("ULF does not contain a root element")

    system_info = ET.tostring(
        dict_to_xml("SystemInfo", system_info_data(machine)),
        encoding="utf-8",
        short_empty_elements=True,
    )
    # Preserve the server-signed ULF byte-for-byte; only insert unsigned SystemInfo.
    return b"".join((ulf[: root_end + 1], system_info, ulf[root_end + 1 :]))


class UnityPersonalActivator:
    def __init__(
        self,
        endpoints: Endpoints = DEFAULT_ENDPOINTS,
        opener: Callable[..., object] = urllib.request.urlopen,
        timeout: float = 60.0,
    ) -> None:
        self.endpoints = endpoints
        self.opener = opener
        self.timeout = timeout

    def _request(
        self,
        url: str,
        *,
        method: str,
        body: bytes | None = None,
        headers: dict[str, str] | None = None,
    ) -> bytes:
        request = urllib.request.Request(
            url, data=body, headers=headers or {}, method=method
        )
        try:
            response = self.opener(request, timeout=self.timeout)
            with response:
                return response.read()
        except urllib.error.HTTPError as error:
            target = urllib.parse.urlsplit(url)
            raise ActivationError(
                f"{method} {target.netloc}{target.path} returned HTTP {error.code}"
            ) from None
        except urllib.error.URLError as error:
            target = urllib.parse.urlsplit(url)
            raise ActivationError(
                f"{method} {target.netloc}{target.path} failed: {error.reason}"
            ) from None

    def _json_request(
        self,
        url: str,
        *,
        method: str,
        payload: object,
        headers: dict[str, str] | None = None,
    ) -> dict[str, object]:
        request_headers = {"Content-Type": "application/json"}
        if headers:
            request_headers.update(headers)
        raw = self._request(
            url,
            method=method,
            body=json.dumps(payload, separators=(",", ":")).encode("utf-8"),
            headers=request_headers,
        )
        try:
            decoded = json.loads(raw)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ActivationError("Unity returned malformed JSON") from error
        if not isinstance(decoded, dict):
            raise ActivationError("Unity returned a non-object JSON response")
        return decoded

    def login(self, email: str, password: str) -> str:
        response = self._json_request(
            f"{self.endpoints.core}/api/login",
            method="POST",
            payload={
                "grant_type": "password",
                "username": email,
                "password": password,
            },
            headers={
                "Accept": "application/json, text/plain, */*",
                "Origin": "file://",
                "User-Agent": UNITY_WEB_USER_AGENT,
            },
        )
        token = response.get("access_token")
        if not isinstance(token, str) or not token:
            raise ActivationError("Unity login response did not contain an access token")
        return token

    @staticmethod
    def _editor_headers(access_token: str) -> dict[str, str]:
        return {
            "Accept": "*/*",
            "Accept-Encoding": "identity",
            "Authorization": f"Bearer {access_token}",
            "Content-Type": "text/xml",
            "User-Agent": UNITY_EDITOR_USER_AGENT,
            "X-UNITY-VERSION": UNITY_VERSION,
        }

    @staticmethod
    def _transaction_headers(access_token: str) -> dict[str, str]:
        return {
            "Accept": "application/json, text/plain, */*",
            "Authorization": f"Bearer {access_token}",
            "Content-Type": "application/json",
            "Origin": "file://",
            "User-Agent": UNITY_WEB_USER_AGENT,
        }

    def activate(self, alf: bytes, email: str, password: str) -> bytes:
        machine_id = validate_alf(alf)
        access_token = self.login(email, password)
        transaction_id = secrets.token_hex(16)
        authorization_xml = self._editor_headers(access_token)
        authorization_json = self._transaction_headers(access_token)

        poll_response = self._request(
            f"{self.endpoints.license}/update/poll?cmd=9&tx_id={transaction_id}",
            method="POST",
            body=alf,
            headers=authorization_xml,
        )
        _transaction_root(poll_response)

        transaction_url = f"{self.endpoints.license}/api/transactions/{transaction_id}"
        transaction = self._json_request(
            transaction_url,
            method="PUT",
            payload={"transaction": {"serial": {"type": "personal"}}},
            headers=authorization_json,
        )
        transaction_body = transaction.get("transaction")
        if not isinstance(transaction_body, dict):
            raise ActivationError("Unity did not return an activation transaction")

        survey = transaction_body.get("survey")
        if (
            isinstance(survey, dict)
            and survey.get("required") is True
            and survey.get("answered") is not True
        ):
            transaction = self._json_request(
                transaction_url,
                method="PUT",
                payload={
                    "transaction": {
                        "serial": {"type": "personal"},
                        "survey_answer": {"skipped": True},
                    }
                },
                headers=authorization_json,
            )
            transaction_body = transaction.get("transaction")
            if not isinstance(transaction_body, dict):
                raise ActivationError("Unity did not return the updated transaction")

        rx = transaction_body.get("rx")
        if not isinstance(rx, str) or not rx:
            raise ActivationError("Unity transaction did not contain an RX value")
        ulf = self._activation_request(
            command="9",
            transaction_id=transaction_id,
            rx=rx,
            body=alf,
            headers=authorization_xml,
        )
        validate_ulf(ulf, machine_id)
        return ulf

    def return_license(
        self,
        ulf: bytes,
        machine: UnityMachineInfo,
        email: str,
        password: str,
    ) -> bytes:
        machine_id = validate_ulf(ulf)
        payload = build_return_payload(ulf, machine)
        access_token = self.login(email, password)
        headers = self._editor_headers(access_token)
        transaction_id = secrets.token_hex(16)
        poll_response = self._request(
            f"{self.endpoints.license}/update/poll?cmd=3&tx_id={transaction_id}",
            method="POST",
            body=payload,
            headers=headers,
        )
        transaction = _transaction_root(poll_response)
        rx = _child_text(transaction, "Rx")
        if not rx:
            raise ActivationError("Unity return transaction did not contain an RX value")
        response = self._activation_request(
            command="3",
            transaction_id=transaction_id,
            rx=rx,
            body=payload,
            headers=headers,
        )
        validate_ulf(response, machine_id)
        return response

    def _activation_request(
        self,
        *,
        command: str,
        transaction_id: str,
        rx: str,
        body: bytes,
        headers: dict[str, str],
    ) -> bytes:
        query = urllib.parse.urlencode(
            {"CMD": command, "TX": transaction_id, "RX": rx}
        )
        return self._request(
            f"{self.endpoints.activation}/license.fcgi?{query}",
            method="POST",
            body=body,
            headers=headers,
        )


def _parse_xml(data: bytes, label: str) -> ET.Element:
    try:
        return ET.fromstring(data)
    except ET.ParseError as error:
        raise ActivationError(f"{label} is not valid XML") from error


def _local_name(element: ET.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def _child_text(root: ET.Element, name: str) -> str | None:
    for child in root:
        if _local_name(child) == name:
            return child.text
    return None


def _transaction_root(data: bytes) -> ET.Element:
    root = _parse_xml(data, "Unity transaction response")
    if _local_name(root) != "Transaction":
        raise ActivationError("Unity rejected the license transaction")
    return root


def _terms_license(root: ET.Element, label: str) -> ET.Element:
    license_element = root.find("./License[@id='Terms']")
    if license_element is None:
        raise ActivationError(f"{label} does not contain the Terms license")
    return license_element


def validate_alf(alf: bytes) -> str:
    root = _parse_xml(alf, "ALF")
    license_element = _terms_license(root, "ALF")
    machine_id_element = license_element.find("./MachineID")
    machine_id = (
        machine_id_element.get("Value") if machine_id_element is not None else None
    )
    if not machine_id:
        raise ActivationError("ALF does not contain a machine ID")

    bindings: dict[str, str] = {}
    for binding in license_element.findall("./MachineBindings/Binding"):
        key = binding.get("Key")
        value = binding.get("Value")
        if key and value:
            bindings[key] = value
    expected = machine_id_for_bindings(bindings)
    if not secrets.compare_digest(machine_id, expected):
        raise ActivationError("ALF machine ID does not match its machine bindings")
    version = license_element.find("./UnityVersion")
    if version is None or version.get("Value") != UNITY_VERSION:
        raise ActivationError(f"ALF is not for Unity {UNITY_VERSION}")
    return machine_id


def validate_ulf(ulf: bytes, expected_machine_id: str | None = None) -> str:
    root = _parse_xml(ulf, "ULF")
    license_element = _terms_license(root, "ULF")
    machine_id_element = license_element.find("./MachineID")
    machine_id = (
        machine_id_element.get("Value") if machine_id_element is not None else None
    )
    if not machine_id:
        raise ActivationError("ULF does not contain a machine ID")
    if expected_machine_id is not None and not secrets.compare_digest(
        machine_id, expected_machine_id
    ):
        raise ActivationError("ULF machine ID does not match the ALF")
    entitlements = {
        entitlement.get("Tag")
        for entitlement in license_element.findall("./Entitlements/Entitlement")
    }
    if "UnityPersonal" not in entitlements:
        raise ActivationError("ULF does not contain Unity Personal entitlement")
    signature = root.find("./{http://www.w3.org/2000/09/xmldsig#}Signature")
    if signature is None:
        raise ActivationError("ULF does not contain Unity's XML signature")
    return machine_id


def _write_private(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=path.name + ".", dir=path.parent)
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "wb") as output:
            output.write(data)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary_name, path)
        os.chmod(path, 0o600)
    except BaseException:
        try:
            os.close(descriptor)
        except OSError:
            pass
        try:
            os.unlink(temporary_name)
        except OSError:
            pass
        raise


def _credentials() -> tuple[str, str]:
    email = os.environ.get("UNITY_EMAIL", "")
    password = os.environ.get("UNITY_PASSWORD", "")
    if not email or not password:
        raise ActivationError("UNITY_EMAIL and UNITY_PASSWORD are required")
    return email, password


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Manage ephemeral Unity 5.6 Personal licenses"
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    activate_parser = subparsers.add_parser("activate")
    activate_parser.add_argument("--output", type=Path, required=True)

    return_parser = subparsers.add_parser("return")
    return_parser.add_argument("--license", type=Path, required=True)

    arguments = parser.parse_args()
    try:
        email, password = _credentials()
        machine = WineMachineCollector().collect()
        activator = UnityPersonalActivator()
        if arguments.command == "activate":
            alf = build_alf(machine)
            validate_alf(alf)
            ulf = activator.activate(alf, email, password)
            _write_private(arguments.output, ulf)
            print("Unity Personal activation succeeded")
            return 0

        ulf = arguments.license.read_bytes()
        activator.return_license(ulf, machine, email, password)
        print("Unity license returned")
        return 0
    except (ActivationError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
