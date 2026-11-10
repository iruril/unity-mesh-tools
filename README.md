# Unity Mesh Tools

Unity 2022.3 LTS와 Unity 6에서 사용하는 공용 스킨드 메시 절단 편집기 패키지입니다.

## 1.2.1

`Tools > Skeletal Mesh Cutter`에서 공용 모델 편집 창을 엽니다.

- 모델 루트를 지정하면 하위 SkinnedMeshRenderer가 기본으로 모두 선택됩니다. 메시를 개별 선택해 같은 평면으로 절단할 수 있습니다.
- 마스크 탭의 `마스크 편집 메시`에서 메시를 전환하며 독립적으로 칠합니다. 출력 탭에서 공용 프리셋을 저장하면 메시 선택, 각 메시의 마스크, 콜라이더 제외 설정을 함께 저장합니다.
- 절단 탭에서 기존 또는 자동 래그돌을 선택한 뒤 `씬에서 콜라이더 선택`을 누릅니다. 초록은 포함, 빨강은 제외이며 내부나 선을 클릭하면 해당 콜라이더 하나가 전환됩니다. `모두 포함`, `모두 제외`, Esc 종료를 지원합니다.
- 콜라이더 제외는 PART에 적용됩니다. REMAIN은 자체 절단 결과의 물리를 사용합니다.
- 물리 콜라이더가 모두 제외된 본은 출력 Rigidbody와 해당 본에 연결되는 Joint도 제거합니다. 본 Transform과 스킨 연결은 보존합니다.
- 출력은 메시별 이름, 부모 계층, 재질과 본 연결을 보존합니다. 선택한 메시에서 PART와 REMAIN을 생성하며, 원본 적용과 씬 생성은 출력 탭의 설정을 따릅니다.
- 기존 단일 메시 공용 프리셋도 불러올 수 있습니다. TheIRONY의 부위별 배치 프리셋은 게임 프로젝트의 전용 형식입니다.

공용 메뉴는 `Tools > Skeletal Mesh Cutter` 하나입니다. 기존 `SkinnedMeshCutterLauncher.Open`과 `WindowFactory` API는 프로젝트 전용 메뉴의 호환성을 위해 유지합니다. 공용 메뉴는 등록된 전용 창과 독립적으로 공용 모델 창을 엽니다.

1.2.1은 MIT 라이선스로 제공하며, 설치할 때 `v1.2.1` 태그를 지정합니다.

## 로컬 개발과 프로젝트 연동

StylishActionGame은 아래 설치 절의 `v1.2.1` Git URL을 사용합니다. TheIRONY는 현재 1.2.0을 유지하며 전용 메뉴 정리와 업데이트는 별도로 진행합니다. 패키지 자체를 수정할 때만 이 저장소의 로컬 폴더로 전환합니다. 로컬 연결에서는 코드를 수정한 뒤 Unity에서 Refresh하면 적용됩니다.

```text
file:<저장소의 절대 경로>/Packages/com.mesh-tools.skinned-mesh-cutter
```

`Tools > Skeletal Mesh Cutter`는 공용 창이고, TheIRONY의 기존 시체·파츠 창은 게임 전용 프리셋과 런타임 연동을 유지하며 다음 공개 API를 호출합니다. 네임스페이스는 `MeshTools.Dismemberment.Editor`입니다.

| API | 프로젝트에서 담당할 부분 |
| --- | --- |
| `SkinnedMeshCutMultiSource.Cut` | 기준 렌더러, 대상 렌더러와 대상 메시의 마스크 전달. 반환된 `Result`는 사용 후 Dispose. |
| `SkinnedMeshCutMaskEditor` | `CreateInstance`로 세션 생성, `Bind`, `DrawSettings`/`DrawScene`, `ConsumeChanges`로 프리셋 동기화. 종료 시 DestroyImmediate. `DisplayMesh`는 세션 소유. |
| `SkinnedMeshCutColliderGizmo` | 콜라이더 표시 행렬과 포함 여부에 따른 색상 전달, `Raycast`/`DistanceToMouse`로 선택. |
| `SkinnedMeshCutRagdoll.Prepare` | 마지막 인자로 모델 렌더러 배열을 전달해 추가 메시 전용 물리 본도 등록. |
| `SkinnedMeshCutRagdoll.CopyTo` | 다중 출력 메시의 표면을 함께 사용. 선택적인 콜라이더 필터는 원본 콜라이더 기준. |
| `SkinnedMeshCutRagdoll.RemoveEmptyBodies` | 제외에 영향을 받은 출력 Rigidbody만 전달. 소유 물리 콜라이더가 없으면 남은 트리거·자체/연결 Joint와 강체를 정리하며, 제거 전 콜백에서 게임 전용 참조를 해제. 런타임 센서는 호출 후 추가. |

TheIRONY의 기존 클래스 이름과 `.meta` GUID를 유지한 어댑터로 연결하므로 배치 프리셋을 변환할 필요가 없습니다. 로컬 개발 시에는 각 PC의 저장소 경로를 사용하고, 검증 후 프로젝트를 배포 태그로 다시 연결합니다.

개발 검증 프로젝트는 Unity 2022.3의 TheIRONY와 Unity 6의 StylishActionGame입니다. TheIRONY의 게임 연동 검사 51개와 공용 패키지 테스트 73개가 통과했으며, StylishActionGame의 Unity 6000.3.23f1 / ProBuilder 6.1.4에서도 패키지 EditMode 테스트 73개가 모두 통과했습니다.

1.2.1부터 배포 패키지에는 테스트 어셈블리나 검증 스크립트를 포함하지 않습니다. 개발 검증은 `Development/Tests/Editor`를 검증용 Unity 프로젝트의 `Assets/Editor` 아래에 복사해서 실행합니다. 소비 프로젝트에는 테스트용 `testables` 설정이 필요하지 않습니다.

## 1.2.1 설치

GitHub private 저장소에 접근할 수 있는 계정으로 Git 인증을 준비하고,
Unity Package Manager에서 다음 URL을 설치합니다.
인증 토큰을 설치 URL이나 프로젝트 파일에 넣지 않습니다.

```text
https://github.com/iruril/unity-mesh-tools.git?path=/Packages/com.mesh-tools.skinned-mesh-cutter#v1.2.1
```

| Unity | 프로젝트의 ProBuilder 버전 |
| --- | --- |
| 2022.3 LTS | 5.2.4 |
| 6.3 LTS | 6.1.4 |

패키지는 ProBuilder 5.2.4를 최소 의존성으로 선언합니다. Unity 6 프로젝트는
ProBuilder 6.1.4를 직접 설치하여 유지합니다. Unity API 차이는 버전 분기로 처리합니다.

## 래그돌 생성기

1. 프로젝트에 Bz Ragdoll Helper가 설치되어 있으면 해당 설치본의 생성 API를 우선 사용합니다.
2. Bz가 없으면 에디터에 포함된 Unity Ragdoll Builder를 사용합니다.
3. 기존 래그돌 참조를 복사하는 기능은 두 생성기 설치 여부와 관계없이 동작합니다.

래그돌 생성기는 프로젝트에 설치된 도구를 호출합니다.
Bz는 선택 사항이며, 필요하면 프로젝트에 별도로 설치합니다.
Unity 내장 생성기는 기본 11개 강체 구성을 사용합니다. 별도 손·발 강체 추가 옵션은 Bz 사용 시에만 활성화됩니다.

## 패키지 업데이트

절단 패키지의 Git URL을 위의 1.2.1 태그로 업데이트합니다.
프로젝트에서 별도로 설치한 래그돌 생성기는 계속 사용할 수 있습니다.

## 절단 콜라이더

- 절단면과 교차하지 않는 콜라이더는 원본 종류와 설정을 보존합니다.
- 로컬 축과 일치하는 절단은 BoxCollider의 중심과 크기만 줄입니다.
- 실제로 비스듬하게 잘린 Box와 잘린 Sphere·Capsule만 Convex MeshCollider로 변환합니다.
- 기존 MeshCollider는 원본 메시와 설정을 유지합니다.

패키지 수정은 이후 생성하는 출력에 적용됩니다. 이미 생성한 출력은 같은 절단 설정으로 다시 생성해야 합니다.

## 구성과 개발

- `Packages/com.mesh-tools.skinned-mesh-cutter/Editor`: 절단, 캡 UV, 마스크 편집, 래그돌 출력.
- `Development/Tests/Editor`: 개발용 공용 회귀 테스트. UPM 배포 경로 밖에 있어 설치 패키지에 포함되지 않습니다.
- 게임 전용 편집 창 확장, 씬, 모델 및 생성 에셋은 각 게임 프로젝트에서 관리합니다.

이 저장소를 수정하고 Unity 2022.3과 Unity 6에서 검증한 뒤 새 태그를 배포합니다.
기존 태그를 이동하지 않고, 프로젝트별 `manifest.json`과 `packages-lock.json`을 함께 커밋합니다.
설치된 `Library/PackageCache` 사본을 직접 수정하지 않습니다.

## 라이선스

툴 소스는 MIT License로 배포합니다. 전문은 저장소의 `LICENSE`와 패키지의 `LICENSE.md`에 포함합니다. 의존 패키지에는 각 패키지의 라이선스가 적용됩니다.
