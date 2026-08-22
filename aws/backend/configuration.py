from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class ChzzkCredentials:
    client_id: str
    client_secret: str


@dataclass(frozen=True)
class ChzzkConfiguration:
    companion: ChzzkCredentials
    mylamb: ChzzkCredentials
    source: str


class ConfigurationError(RuntimeError):
    pass


class ConfigurationProvider:
    def load(self) -> ChzzkConfiguration:
        raise NotImplementedError


class EnvironmentConfigurationProvider(ConfigurationProvider):
    """Local/dev provider. Reads credentials only from process environment variables."""

    def _required(self, name: str) -> str:
        value = os.environ.get(name, "").strip()
        if not value:
            raise ConfigurationError(f"Required environment variable is missing: {name}")
        return value

    def load(self) -> ChzzkConfiguration:
        return ChzzkConfiguration(
            companion=ChzzkCredentials(
                client_id=self._required("CHZZK_CLIENT_ID"),
                client_secret=self._required("CHZZK_CLIENT_SECRET"),
            ),
            mylamb=ChzzkCredentials(
                client_id=self._required("MYLAMB_CHZZK_CLIENT_ID"),
                client_secret=self._required("MYLAMB_CHZZK_CLIENT_SECRET"),
            ),
            source="environment",
        )


class AwsConfigurationProvider(ConfigurationProvider):
    """Production provider. Reads Client IDs from SSM and Client Secrets from Secrets Manager.

    boto3 is imported lazily so local environment-only development does not require the AWS SDK.
    The same provider can be tested from a developer PC before Lambda deployment by using a
    configured AWS CLI profile/credentials.
    """

    DEFAULT_COMPANION_ID_PARAM = "/cotl/prod/chzzk/companion/client-id"
    DEFAULT_MYLAMB_ID_PARAM = "/cotl/prod/chzzk/mylamb/client-id"
    DEFAULT_COMPANION_SECRET = "/cotl/prod/chzzk/companion"
    DEFAULT_MYLAMB_SECRET = "/cotl/prod/chzzk/mylamb"

    def __init__(self, region_name: Optional[str] = None, profile_name: Optional[str] = None):
        try:
            import boto3  # type: ignore
        except ImportError as exc:
            raise ConfigurationError(
                "AWS configuration provider requires boto3. Install it with: py -m pip install boto3"
            ) from exc

        session_kwargs = {}
        if profile_name:
            session_kwargs["profile_name"] = profile_name
        if region_name:
            session_kwargs["region_name"] = region_name
        self._session = boto3.Session(**session_kwargs)
        self._ssm = self._session.client("ssm")
        self._secrets = self._session.client("secretsmanager")

    @staticmethod
    def _env_name(name: str, default: str) -> str:
        return os.environ.get(name, default).strip() or default

    def _parameter(self, name: str) -> str:
        response = self._ssm.get_parameter(Name=name, WithDecryption=False)
        value = str(response["Parameter"]["Value"]).strip()
        if not value:
            raise ConfigurationError(f"SSM parameter is empty: {name}")
        return value

    def _secret_value(self, secret_id: str) -> str:
        response = self._secrets.get_secret_value(SecretId=secret_id)
        raw = response.get("SecretString")
        if raw is None:
            raise ConfigurationError(f"SecretString is missing: {secret_id}")

        # Preferred form: {"clientSecret":"..."}. A plain string is also accepted.
        try:
            parsed = json.loads(raw)
            if isinstance(parsed, dict):
                for key in ("clientSecret", "client_secret", "secret"):
                    value = parsed.get(key)
                    if isinstance(value, str) and value.strip():
                        return value.strip()
        except json.JSONDecodeError:
            pass

        if str(raw).strip():
            return str(raw).strip()
        raise ConfigurationError(f"Secret is empty: {secret_id}")

    def load(self) -> ChzzkConfiguration:
        companion_id_param = self._env_name(
            "COTL_SSM_COMPANION_CLIENT_ID_PARAM", self.DEFAULT_COMPANION_ID_PARAM
        )
        mylamb_id_param = self._env_name(
            "COTL_SSM_MYLAMB_CLIENT_ID_PARAM", self.DEFAULT_MYLAMB_ID_PARAM
        )
        companion_secret_id = self._env_name(
            "COTL_SECRET_COMPANION_ID", self.DEFAULT_COMPANION_SECRET
        )
        mylamb_secret_id = self._env_name(
            "COTL_SECRET_MYLAMB_ID", self.DEFAULT_MYLAMB_SECRET
        )

        return ChzzkConfiguration(
            companion=ChzzkCredentials(
                client_id=self._parameter(companion_id_param),
                client_secret=self._secret_value(companion_secret_id),
            ),
            mylamb=ChzzkCredentials(
                client_id=self._parameter(mylamb_id_param),
                client_secret=self._secret_value(mylamb_secret_id),
            ),
            source="aws-ssm-secretsmanager",
        )


def load_chzzk_configuration(provider_name: Optional[str] = None) -> ChzzkConfiguration:
    mode = (provider_name or os.environ.get("COTL_CONFIG_PROVIDER", "local")).strip().lower()
    if mode in ("local", "env", "environment"):
        return EnvironmentConfigurationProvider().load()
    if mode in ("aws", "ssm", "secretsmanager"):
        return AwsConfigurationProvider(
            region_name=os.environ.get("AWS_REGION") or os.environ.get("AWS_DEFAULT_REGION"),
            profile_name=os.environ.get("AWS_PROFILE"),
        ).load()
    raise ConfigurationError(f"Unknown COTL_CONFIG_PROVIDER: {mode}")
