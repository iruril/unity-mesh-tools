# Skeletal Mesh Cutter

Unity 2022.3 LTS와 Unity 6에서 스킨드 메시를 절단하고 마스크, 단면 UV, 래그돌을 설정하는 에디터 패키지입니다.

## 설치

Unity Package Manager의 **Add package from git URL**에서 다음 URL을 입력합니다.

```text
https://github.com/iruril/unity-mesh-tools.git?path=/Packages/com.mesh-tools.skinned-mesh-cutter#v1.2.5
```

| Unity 버전 | ProBuilder 버전 |
| --- | --- |
| 2022.3 LTS | 5.2.4 |
| 6.3 LTS | 6.1.4 |

패키지는 ProBuilder 5.2.4를 최소 의존성으로 선언합니다. Unity 6에서는 ProBuilder 6.1.4를 설치합니다.

## 모델과 메시 선택

1. `Tools > Skeletal Mesh Cutter`를 엽니다.
2. `모델 루트`에 작업할 모델을 지정합니다. 하위 SkinnedMeshRenderer가 기본으로 모두 선택됩니다.
3. 절단할 메시를 선택하고 작업 포즈를 지정합니다.
4. `절단` 탭에서 절단 평면과 대상 본을 설정합니다. 선택한 메시에는 같은 월드 평면이 적용됩니다.

출력은 메시별 이름, 부모 계층, 재질과 본 연결을 보존합니다.

## 마스크와 단면 UV

- `마스크` 탭의 `마스크 편집 메시`에서 메시를 전환하며 독립적으로 칠합니다.
- `단면·UV` 탭에서 절단면의 재질과 UV를 조정합니다.
- `출력` 탭에서 공용 프리셋을 저장하면 메시 선택, 메시별 마스크, 콜라이더 제외 설정을 함께 저장합니다.
- 기존 단일 메시 공용 프리셋도 불러올 수 있습니다.

## 물리 본과 콜라이더

1. `절단` 탭에서 기존 래그돌 또는 자동 래그돌을 선택합니다.
2. `씬에서 콜라이더 선택`을 누릅니다. 초록은 포함, 빨강은 제외 상태입니다.
3. 콜라이더 내부나 선을 클릭하면 해당 콜라이더 하나를 전환합니다. `모두 포함`, `모두 제외`도 사용할 수 있습니다.
4. Esc를 눌러 선택을 마칩니다.

콜라이더 제외는 분리 파츠인 PART에 적용됩니다. 나머지 메시인 REMAIN은 자체 절단 결과의 물리를 사용합니다.
물리 콜라이더가 모두 제외된 본은 출력 Rigidbody와 연결 Joint도 제거합니다. 본 Transform과 스킨 연결은 보존합니다.

절단면과 교차하지 않는 콜라이더는 원래 종류와 설정을 유지합니다. 축과 일치하는 BoxCollider 절단은 중심과 크기를 조정하고, 비스듬한 Box 및 잘린 Sphere·Capsule은 Convex MeshCollider로 변환합니다. 기존 MeshCollider는 원본 메시와 설정을 유지합니다.

## 래그돌 자동 생성

- Bz Ragdoll Helper가 설치되어 있으면 해당 생성기를 우선 사용합니다.
- Bz가 없으면 Unity 내장 Ragdoll Builder를 사용합니다. Bz 설치는 선택 사항입니다.
- Unity 내장 생성기는 기본 11개 강체를 구성합니다. 별도 손·발 강체 추가 옵션은 Bz 사용 시에만 활성화됩니다.
- 기존 래그돌 참조를 복사하는 기능은 생성기 설치 여부와 관계없이 사용할 수 있습니다.

## 출력과 업데이트

`출력` 탭에서 PART와 REMAIN을 생성하고 메시·프리팹을 저장합니다. 원본 적용과 씬 생성 여부도 이 탭에서 설정합니다.

패키지를 업데이트하려면 Package Manager에서 위 Git URL의 태그를 원하는 버전으로 지정합니다.
새 설정과 패키지 수정 사항은 이후 생성하는 출력에 적용되므로, 이미 생성한 출력은 같은 절단 설정으로 다시 생성해야 합니다.

## 전용 에디터에 연결하기

`MeshTools.Dismemberment.Editor` 네임스페이스의 API를 사용합니다.

- `SkinnedMeshCutColliderUtility`: `Copy`로 콜라이더 하나를 복사하고, `CopyFrom`으로 같은 로컬 좌표계를 쓰는 대상의 콜라이더 구성을 교체합니다. `IncludeAttachment`는 렌더러 범위로 박스 또는 캡슐을 확장합니다. 캡슐은 반지름을 유지하며 길이와 중심만 바뀝니다. 보관 위치와 장착 위치가 다르면 월드 좌표를 콜라이더 로컬 좌표로 옮기는 행렬을 전달합니다.
- `SkinnedMeshCutModelUtility`: 하위 메시 조회, 기준 메시 선택, 저장된 경로 선택과 마스크 검증. `SelectRenderers`의 키 함수에 기존 프리셋의 경로 계산을 전달할 수 있습니다.
- `SkinnedMeshCutPreviewStage.IncludeRenderer`: 열린 미리보기 스테이지에 렌더러를 복제하고 본을 연결합니다. 같은 소스는 기존 복제본을 반환합니다.
- `SkinnedMeshCutModelPreview`: 절단 결과와 마스크 표시를 생성합니다. 갱신 전 `Clear`, 소유 편집기를 닫을 때 `Dispose`를 호출합니다. `AddMask`로 전달한 메시의 소유권은 호출자에게 있습니다.
- `SkinnedMeshCutterWindowBase`를 상속하면 `ModelOutputMaterials`와 `CreateModelOutputRenderer`로 출력 재질과 본 연결을 재사용할 수 있습니다. 출력 메시와 저장 자산은 호출자가 관리합니다.

## 라이선스

MIT License로 제공합니다. 전문은 저장소의 `LICENSE`와 패키지의 `LICENSE.md`에 있습니다. 의존 패키지에는 각 패키지의 라이선스가 적용됩니다.
