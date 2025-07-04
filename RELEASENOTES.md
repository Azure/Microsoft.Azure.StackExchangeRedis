# Release Notes

## Unreleased

## 3.2.2
- Refresh token within two heartbeats of expiration even if less than 75% of its lifetime has passed (see https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/70)([commit c687907 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/commit/c687907c520d80c609631a88ef19c0d9722b2284))
- Update dependency package versions ([#81 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/81))

## 3.2.1
- Update dependency package versions to get a fix for a vulnerability (see https://github.com/dotnet/announcements/issues/329) ([#73 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/73))
- Minor improvements to sample app logging and comments ([#72 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/72))

## 3.2.0
- Add support for overriding the Redis scope/audience to address ([#60](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/60)) ([#67 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/67))
- Update to latest .NET SDK and dependencies ([#66 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/66))

## 3.1.0
- Fix [#51](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/51): Preserve user name for reauthentication ([#52 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/52))
- Fix [#39](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/39): Enable Source Link and Central Package Management ([#54 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/54))

## 3.0.0
- Fix [#17](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/17): Eliminate the need to specify a principalId/objectId/userName ([#48 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/48))
- Add support for Subject Name + Issuer (SNI) certificate authentication ([#46 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/46))
- Update dependency package versions ([#47 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/47))

## 2.0.0
- Fix [#2](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/2), [#25](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/25): Add TokenCredential/DefaultAzureCredential support ([#10 by lsannicolas](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/10))
- Fix [#13](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/13), [#20](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/20), [#22](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/22): Support for certificates and sovereign clouds with Service Principal authentication ([#33 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/33))
- Fix [#4](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/4): Enable strong name signing ([#31 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/31))
- Fix [#6](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/6), [#15](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/15), [#18](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/18), [#34](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/issues/34): Upgrade Microsoft.Identity.Client package reference ([#7 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/7))
- Dynamic token refresh margin ([#3 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/3))
- Upgrade PackageReferences to latest ([#30 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/30))
- Update 'AAD' terminology to 'Microsoft Entra ID' ([#29 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/29))
- Link to Azure identity objects documentation ([#24 by sebastienros](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/24))
- Remove unnecessary await in README ([#21 by eerhardt](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/21))

## 1.1.0
- AAD (Microsoft Entra ID) Support ([#1 by philon-msft](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/pull/1))
